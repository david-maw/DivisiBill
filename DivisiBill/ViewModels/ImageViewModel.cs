using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Models;
using DivisiBill.Services;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;

namespace DivisiBill.ViewModels;

/// <summary>
/// This is where all the image manipulation takes place, the user can select a new image or take a picture as many times as they like
/// but eventually whatever image is showing when they exit is the one we keep. The current intermediate image is in <see cref="Meal.TempImageFilePath"/>.
/// The image is in <see cref="Meal.ImagePath"/> and any deleted image is in the <see cref="Meal.DeletedItemFolderPath"/> along with deleted Meal files. 
/// </summary>
public partial class ImageViewModel : ObservableObjectPlus, IQueryAttributable
{
    #region Life Cycle
    /// <summary>
    /// Flag that a new image has been selected or the old one has been deleted 
    /// </summary>
    private bool imageChanged = false;
    /// <summary>
    /// The name of the picture that was selected, used to speed up debugging OCR operations
    /// </summary>
    private string browsedPictureName = null;
    /// <summary>
    /// The replacement image stream provided by the camera page
    /// </summary>
    private Stream replacementImageStream = null;
    /// <summary>
    /// Whether the image page should immediately start a camera page
    /// </summary>
    private bool startWithCamera = false;

    public async Task ProcessQueryAsync()
    {
        // Kludge to work around ApplyQueryAttributes being fired at the wrong time, this gives it an opportunity to fire
        // see: https://github.com/dotnet/maui/issues/24241
        await Task.Delay(50);

        if (startWithCamera)
            await App.PushAsync(Routes.CameraPage);
        else
            await Load();
    }
    public async Task Load()
    {
        // Ensure the storage folder exists before attempting to use it, this is a no-op if the folder already exists
        Directory.CreateDirectory(Meal.ImageFolderPath);

        // Evaluate whether we've been called to show a new image or the existing one (or incorrectly called twice)
        if (replacementImageStream is not null && replacementImageStream.Position < replacementImageStream.Length)
        {
            // There is a new image, convert it to grayscale and shrink it as needed
            await LoadImageStreamAsync(replacementImageStream);

            PreviewImageSource = ImageSource.FromStream(() => File.OpenRead(Meal.TempImageFilePath));
        }
        else if (Meal.CurrentMeal.HasImage)
            PreviewImageSource = ImageSource.FromStream(() => File.OpenRead(Meal.CurrentMeal.ImagePath));
        else
        {
            PreviewImageSource = null;
        }
        // Make sure Image File Status is initialized to correct value
        Meal.CurrentMeal.CheckImageFiles();
        // Track subsequent changes
        Meal.CurrentMeal.Summary.PropertyChanged += CurrentMeal_PropertyChanged;
    }

    /// <summary>
    /// Persist the current image (or lack of one) with the current Meal
    /// </summary>
    public void Store()
    {
        if (HasPreviewImage)
        {
            if (imageChanged)
            {
                // Move working image copy to current bill
                // There's a delicate handshake here if the Meal.CurrentMeal is frozen, because that means its name will change as soon as it is thawed
                // We'd like all that to happen before storing the image because the default name will change.
                Meal.CurrentMeal.MarkAsChanged();
                // save the file into local storage
                Meal.CurrentMeal.ReplaceImage(Meal.TempImageFilePath);
            }
        }
        else
            Meal.CurrentMeal.DeleteImage();
        imageChanged = false;
        Meal.CurrentMeal.Summary.PropertyChanged -= CurrentMeal_PropertyChanged;
    }

    private void CurrentMeal_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case "HasDeletedImage":
            case "HasImage":
                OnPropertyChanged(e.PropertyName); break;
            default:
                break;
        }
    }

    #endregion
    #region Commands
    /// <summary>
    /// Switch to the camera page so it can provide an image (either from the camera or by browsing) 
    /// </summary>
    [RelayCommand]
    private async Task TakePicture()
    {
        await App.PushAsync(Routes.CameraPage);
    }

    /// <summary>
    /// Pop up UI to browse through the file system for a bill image. If one is selected load it as the current image. 
    /// </summary>
    [RelayCommand]
    private async Task Browse()
    {
        try
        {
            IsBusy = true;
            browsedPictureName = null;
            var photo = await MediaPicker.PickPhotoAsync();
            // We have identified an  image, now copy it to the private storage area, so we have it later, if it is needed
            if (photo is not null)
            {
                await LoadPhotoAsync(photo);
                browsedPictureName = photo.FileName;
                PreviewImageSource = ImageSource.FromStream(() => File.OpenRead(Meal.TempImageFilePath));
            }
        }
        catch (Exception ex)
        {
            IsBusy = false;
            await Utilities.DisplayAlertAsync("Browse", "Could not load photo: " + ex.Message, "cancel");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Run character recognition on the current image iff the user has a license.
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task Ocr()
    {
        if (Services.Billing.ScansLeft <= 0)
            await Utilities.DisplayAlertAsync("Limit", "You have no OCR scan licenses left, purchase more on the Setting page to use OCR", "OK");
        else if (HasPreviewImage)
        {
            if (imageChanged)
                Store();
            var navigationParameter = new ShellNavigationQueryParameters
                {
                    { "ImagePath", Meal.CurrentMeal.ImagePath}
                };
            if (!string.IsNullOrEmpty(browsedPictureName))
                navigationParameter.Add("ScannedBill", ScannedBill.LoadFromFile(browsedPictureName));

            await App.PushAsync(Routes.ScanPage, navigationParameter);
        }
    }

    /// <summary>
    /// Delete the current image and clear the current bill image
    /// </summary>
    [RelayCommand] // If it was working yet this should be [RelayCommand(CanExecute = nameof(HasImage))]
    private void Delete()
    {
        if (Meal.CurrentMeal.HasImage)
        {
            if (Meal.CurrentMeal.Frozen)
                Meal.CurrentMeal.MarkAsChanged();
            Meal.CurrentMeal.DeleteImage();
        }
        PreviewImageSource = null;
        browsedPictureName = null;
        OnPropertyChanged(nameof(HasPreviewImage));
    }

    /// <summary>
    /// UnDelete the current image - beware this is one of the only functions that changes a Meal in place rather than creating a new one.
    /// </summary>
    [RelayCommand] // If it was working yet this should be [RelayCommand(CanExecute = nameof(HasDeletedImage))]
    private void Undelete()
    {
        if (HasDeletedImage)
        {
            Meal.CurrentMeal.TryUndeleteImage();
            PreviewImageSource = ImageSource.FromStream(() => File.OpenRead(Meal.CurrentMeal.ImagePath));
            browsedPictureName = null;
            OnPropertyChanged(nameof(HasPreviewImage));
        }
    }
    #region Controlling the Camera Flash
    /// <summary>
    /// The glyph to use for the flash command - note it is inverted because it is showing what the glyph will do, not what the current state is
    /// </summary>
    [ObservableProperty]
    public partial FontImageSource LightGlyph { get; set; } = (FontImageSource)Application.Current.Resources["GlyphFlashlightOn"];

    [ObservableProperty]
    public partial bool IsLightOn { get; set; } = false;

    [RelayCommand]
    private async Task ChangeLightMode()
    {
        IsLightOn = !IsLightOn;
        LightGlyph = (FontImageSource)(IsLightOn ? Application.Current.Resources["GlyphFlashlightOff"] : Application.Current.Resources["GlyphFlashlightOn"]);
        try
        {
            if (await Flashlight.IsSupportedAsync())
            {
                if (IsLightOn)
                    await Flashlight.TurnOnAsync();
                else
                    await Flashlight.TurnOffAsync();
            }
            else
            {
                Utilities.DebugMsg("Flashlight not supported");
            }
        }
        catch (Exception ex)
        {
            Utilities.DebugMsg("Exception thrown in flashlight operation: " + ex.Message);
        }
    }
    #endregion
    #endregion
    #region Properties
    /// <summary>
    /// Whether there is an image to show
    /// </summary>
    public bool HasPreviewImage => PreviewImageSource is not null;
    public bool HasDeletedImage => Meal.CurrentMeal.HasDeletedImage;
    /// <summary>
    /// The current image as an <see cref="ImageSource"/> 
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewImage))]
    public partial ImageSource PreviewImageSource { get; set; } = null;

    [ObservableProperty]
    private double imageScale = 1;

    [ObservableProperty]
    public partial double ImageTranslationX { get; set; } = 0;

    [ObservableProperty]
    public partial double ImageTranslationY { get; set; } = 0;
    #endregion
    #region Image Load and Store
    /// <summary>
    /// Undo and panning or zooming the user might have done
    /// </summary>
    internal void ResetImageView()
    {
        ImageScale = 1;
        ImageTranslationX = 0;
        ImageTranslationY = 0;
    }
    internal async Task LoadPhotoAsync(FileResult photo)
    {
        // canceled
        if (photo is null)
        {
            IsBusy = false;
            return;
        }
        using var stream = await photo.OpenReadAsync();
        await LoadImageStreamAsync(stream);
    }
    /// <summary>
    /// Take the original image stream and convert it to a simpler one (smaller and monochrome) storing it in a file at <see cref="Meal.TempImageFilePath"/>
    /// </summary>
    /// <param name="stream">The original image</param>
    private async Task LoadImageStreamAsync(Stream stream)
    {
        ResetImageView();
        // Null stream probably means an operation was canceled
        if (stream is null)
        {
            IsBusy = false;
            return;
        }
        using (var newStream = File.Create(Meal.TempImageFilePath))
        {
            if (stream.Length > 200_000) // Arbitrary upper limit on file size below which we just use it as is 
            {
                if (Microsoft.Maui.Devices.DeviceInfo.Platform == DevicePlatform.Android)
                    SkiaConvert(stream, newStream);
                else
                    await ImageSharpConvert(stream, newStream);
            }
            else // It is a small file, just copy it directly
                await stream.CopyToAsync(newStream);
        }
        // Make a snapshot of the image to help with debugging
        if (Utilities.IsDebug)
            File.Copy(Meal.TempImageFilePath, Path.Combine(Meal.ImageFolderPath, "LatestImage.jpg"), true);
        imageChanged = true;
        IsBusy = false;
    }
    #endregion
    #region Image Processing (grayscale and scaling)
    /// <summary>
    /// Convert an image to a smaller, gray scale version of itself to save space, this code runs very slowly (20s+) on Android
    /// in .NET 8 RC2 at least, so there we use the SkiaSharp version for now. It doesn't compress as well, but it's close enough.
    /// </summary>
    /// <param name="imagePath">Path to a file containing the original image data (either from and image picker or camera)</param>
    /// <param name="newStream">The stream to put the new (reduced size, gray scale) data in</param>
    private static async Task ImageSharpConvert(string imagePath, FileStream newStream)
    {
        using Stream stream = File.OpenRead(imagePath);
        await ImageSharpConvert(stream, newStream);
    }
    private static async Task ImageSharpConvert(Stream stream, FileStream newStream)
    {
        using (var image = await SixLabors.ImageSharp.Image.LoadAsync(stream))
        {
            // We have to do a little dance here because it is possible that the EXIF orientation data says to rotate this image by 90 degrees
            // meaning the bitmap width is actually the height of the final image and vice versa
            int exifOrientation = 0;
            if (image.Metadata.ExifProfile is not null)
            {
                foreach (var item in image.Metadata.ExifProfile.Values)
                    if (item.Tag == SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Orientation)
                    {
                        exifOrientation = (UInt16)item.GetValue();
                        break;
                    }
            }
            int newBitmapWidth = 0, newBitmapHeight = 0;
            if (exifOrientation > 4) // 6 is common but 5,7 & 8 all transpose width and height
                newBitmapWidth = 1000;
            else
                newBitmapHeight = 1000;
            image.Mutate(x => x
                .Resize(newBitmapWidth, newBitmapHeight) // Set the width because setting height works strangely
                .Grayscale());
            await image.SaveAsync(newStream, new JpegEncoder() { ColorType = JpegEncodingColor.Luminance });
        }
    }

    /// <summary>
    /// Convert an image to a smaller, gray scale version of itself to save space, this code runs reasonably quickly (around a second 
    /// typically) on Android in .NET 8 RC2 at least, so we use it i place of the ImageSharp version for now. It doesn't compress as well,
    /// but it's close enough.
    /// </summary>
    /// <param name="imagePath">Path to a file containing the original image data (either from and image picker or camera)</param>
    /// <param name="newStream">The stream to put the new (reduced size, gray scale) data in</param>
    private void SkiaConvert(string imagePath, FileStream newStream)
    {
        using Stream stream = File.OpenRead(imagePath);
        SkiaConvert(stream, newStream);
    }
    private void SkiaConvert(Stream stream, FileStream newStream)
    {
        var v = SKImage.FromEncodedData(stream);
        SKBitmap bitmap = SKBitmap.FromImage(v);

        double scale = 1000.0 / Math.Max(bitmap.Width, bitmap.Height);

        var newBitmap = new SKBitmap((int)(bitmap.Width * scale), (int)(bitmap.Height * scale), SKColorType.Gray8, SKAlphaType.Opaque);

        bitmap.ScalePixels(newBitmap, new SKSamplingOptions(SKFilterMode.Linear));

        using (var image = SKImage.FromBitmap(newBitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 100))
        {
            data.SaveTo(newStream);
#if WINDOWS
            // Save the bytes to a file for testing
            var bytes = data.ToArray();
            File.WriteAllBytes(@"c:\temp\divisibilltest.jpg", bytes);
#endif
        }
    }
    #endregion
    #region IQueryAttributable Implementation
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        replacementImageStream = query.TryGetValue("ImageStream", out var streamObject) ? streamObject as Stream : null; // Comes from the camera page
        browsedPictureName = query.TryGetValue("Browsed", out var browsedObject) ? browsedObject as string : null; // From a browse initiated by the camera page
        startWithCamera = query.TryGetValue("StartWithCamera", out object startWithCameraObject) && startWithCameraObject is string s && bool.TryParse(s, out bool b) && b;
    }
    #endregion
}
