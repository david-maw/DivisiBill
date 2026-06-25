using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Models;
using DivisiBill.Services;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
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
    private string? browsedPictureName = null;
    /// <summary>
    /// The replacement image stream provided by the camera page
    /// </summary>
    private Stream? replacementImageStream = null;
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
        // Evaluate whether we've been called to show a new image or the existing one (or incorrectly called twice)
        if (replacementImageStream is not null && replacementImageStream.Position < replacementImageStream.Length)
        {
            // There is a new image, convert it to grayscale and shrink it as needed
            await LoadImageStreamAsync(replacementImageStream);

            PreviewImageSource = ImageSource.FromStream(() => File.OpenRead(Meal.TempImageFilePath));
        }
        else
            PreviewImageSource = Meal.CurrentMeal.HasImage ? ImageSource.FromStream(() => File.OpenRead(Meal.CurrentMeal.ImagePath)) : null;
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

    private void CurrentMeal_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case "HasDeletedImage":
                UndeleteCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(e.PropertyName); break;
            case "HasImage":
                DeleteCommand.NotifyCanExecuteChanged();
                OcrCommand.NotifyCanExecuteChanged();
                RotateRightCommand.NotifyCanExecuteChanged();
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
    private async Task TakePicture() => await App.PushAsync(Routes.CameraPage);

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
            // Parameter to PickPhotosAsync works around https://github.com/dotnet/maui/issues/32535
            FileResult? photo = (await MediaPicker.PickPhotosAsync(new MediaPickerOptions())).FirstOrDefault();
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
    /// Run character recognition on the current image if the user has a license. Alert them if they do not.
    /// </summary>
    /// <returns></returns>
    [RelayCommand(CanExecute = nameof(HasPreviewImage))]
    private async Task Ocr()
    {
        if (Services.Billing.ScansLeft <= 0)
            await Utilities.DisplayAlertAsync("Limit", "You have no OCR scan licenses left, purchase more on the Setting page to use OCR", "OK");
        else if (HasPreviewImage)
        {
            if (imageChanged)
                Store();
            ShellNavigationQueryParameters navigationParameter = new()
            {
                    { "ImagePath", Meal.CurrentMeal.ImagePath}
                };
            if (ScannedBill.LoadFromFile(browsedPictureName) is ScannedBill scannedBill)
                navigationParameter.Add("ScannedBill", scannedBill);

            await App.PushAsync(Routes.ScanPage, navigationParameter);
        }
    }

    /// <summary>
    /// Delete the current image and clear the current bill image
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasPreviewImage))]
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
    }

    /// <summary>
    /// UnDelete the current image - beware this is one of the only functions that changes a Meal in place rather than creating a new one.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasDeletedImage))]
    private void Undelete()
    {
        if (HasDeletedImage)
        {
            Meal.CurrentMeal.TryUndeleteImage();
            PreviewImageSource = ImageSource.FromStream(() => File.OpenRead(Meal.CurrentMeal.ImagePath));
            browsedPictureName = null;
        }
    }

    /// <summary>
    /// Rotate the current image clockwise by 90 degrees
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasPreviewImage))]
    private async Task RotateRight()
    {
        if (!HasPreviewImage)
            return;

        try
        {
            IsBusy = true;
            ResetImageView();

            string sourcePath = imageChanged ? Meal.TempImageFilePath : Meal.CurrentMeal.ImagePath;
            string tempPath = Path.Combine(Path.GetTempPath(), $"rotate_{Guid.NewGuid()}.jpg");

            using (FileStream outputStream = File.Create(tempPath))
            {
                if (Microsoft.Maui.Devices.DeviceInfo.Platform == DevicePlatform.Android)
                    SkiaSharpRotate(sourcePath, outputStream, 90);
                else
                    await ImageSharpRotate(sourcePath, outputStream, 90);
            }

            File.Copy(tempPath, Meal.TempImageFilePath, true);
            File.Delete(tempPath);

            PreviewImageSource = ImageSource.FromStream(() => File.OpenRead(Meal.TempImageFilePath));
            imageChanged = true;
        }
        catch (Exception ex)
        {
            await Utilities.DisplayAlertAsync("Rotate", "Could not rotate image: " + ex.Message, "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }
    #region Controlling the Camera Flash
    /// <summary>
    /// The glyph to use for the flash command - note it is inverted because it is showing what the glyph will do, not what the current state is
    /// </summary>
    [ObservableProperty]
    public partial FontImageSource LightGlyph { get; set; } = (FontImageSource)App.Current.Resources["GlyphFlashlightOn"];

    [ObservableProperty]
    public partial bool IsLightOn { get; set; } = false;

    [RelayCommand]
    private async Task ChangeLightMode()
    {
        IsLightOn = !IsLightOn;
        LightGlyph = (FontImageSource)(IsLightOn ? App.Current.Resources["GlyphFlashlightOff"] : App.Current.Resources["GlyphFlashlightOn"]);
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
    [NotifyCanExecuteChangedFor(nameof(OcrCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(UndeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(RotateRightCommand))]
    public partial ImageSource? PreviewImageSource { get; set; } = null;

    [ObservableProperty]
    public partial double ImageScale { get; set; } = 1;
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
        using Stream stream = await photo.OpenReadAsync();
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
        using (FileStream newStream = File.Create(Meal.TempImageFilePath))
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
    #region Image Shrinking (gray scale and scaling)
    /// <summary>
    /// Convert an image to a smaller, gray scale version of itself to save space, this code runs very slowly (20s+) on Android
    /// in .NET 8 RC2 at least, so there we use the SkiaSharp version for now. It doesn't compress as well, but it's close enough.
    /// </summary>
    /// <param name="stream">Stream containing the original image data (either from and image picker or camera)</param>
    /// <param name="newStream">The stream to put the new (reduced size, gray scale) data in</param>
    private static async Task ImageSharpConvert(Stream stream, FileStream newStream)
    {
        using SixLabors.ImageSharp.Image image = await SixLabors.ImageSharp.Image.LoadAsync(stream);
        // We have to do a little dance here because it is possible that the EXIF orientation data says to rotate this image by 90 degrees
        // meaning the bitmap width is actually the height of the final image and vice versa
        int exifOrientation = 0;
        if (image.Metadata.ExifProfile is not null)
        {
            foreach (IExifValue item in image.Metadata.ExifProfile.Values)
                if (item.Tag == SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Orientation)
                {
                    exifOrientation = (ushort)(item.GetValue() ?? 0);
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
        await image.SaveAsync(newStream, new JpegEncoder() { ColorType = JpegColorType.Luminance });
    }

    /// <summary>
    /// Convert an image to a smaller, gray scale version of itself to save space, this code runs reasonably quickly (around a second 
    /// typically) on Android in .NET 8 RC2 at least, so we use it i place of the ImageSharp version for now. It doesn't compress as well,
    /// but it's close enough.
    /// </summary>
    /// <param name="stream">Stream containing the original image data (either from and image picker or camera)</param>
    /// <param name="newStream">The stream to put the new (reduced size, gray scale) data in</param>
    private void SkiaConvert(Stream stream, FileStream newStream)
    {
        var v = SKImage.FromEncodedData(stream);
        var bitmap = SKBitmap.FromImage(v);

        double scale = 1000.0 / Math.Max(bitmap.Width, bitmap.Height);

        SKBitmap newBitmap = new((int)(bitmap.Width * scale), (int)(bitmap.Height * scale), SKColorType.Gray8, SKAlphaType.Opaque);

        bitmap.ScalePixels(newBitmap, new SKSamplingOptions(SKFilterMode.Linear));

        using var image = SKImage.FromBitmap(newBitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        data.SaveTo(newStream);
#if WINDOWS
        // Save the bytes to a file for testing
        byte[] bytes = data.ToArray();
        File.WriteAllBytes(@"c:\temp\divisibilltest.jpg", bytes);
#endif
    }
    #endregion
    #region Image Rotation
    /// <summary>
    /// Rotate an image using ImageSharp (fast on Windows, slow on Android)
    /// </summary>
    /// <param name="imagePath">Path to a file containing the original image data</param>
    /// <param name="outputStream">The stream to put the rotated image data in</param>
    /// <param name="degrees">The degrees to rotate (e.g., -90 for counter-clockwise, 90 for clockwise)</param>
    private static async Task ImageSharpRotate(string imagePath, FileStream outputStream, float degrees)
    {
        using FileStream inputStream = File.OpenRead(imagePath);
        using SixLabors.ImageSharp.Image image = await SixLabors.ImageSharp.Image.LoadAsync(inputStream);
        image.Mutate(x => x.Rotate(degrees));
        await image.SaveAsync(outputStream, new JpegEncoder() { ColorType = JpegColorType.Luminance });
    }

    /// <summary>
    /// Rotate an image using SkiaSharp (fast on Android, slow on Windows)
    /// </summary>
    /// <param name="imagePath">Path to a file containing the original image data</param>
    /// <param name="outputStream">The stream to put the rotated image data in</param>
    /// <param name="degrees">The degrees to rotate (e.g., -90 for counter-clockwise, 90 for clockwise)</param>
    private void SkiaSharpRotate(string imagePath, FileStream outputStream, float degrees)
    {
        using FileStream inputStream = File.OpenRead(imagePath);
        var sourceImage = SKImage.FromEncodedData(inputStream);
        var sourceBitmap = SKBitmap.FromImage(sourceImage);

        degrees = degrees % 360;
        if (degrees < 0)
            degrees += 360;

        SKBitmap rotatedBitmap;
        if (degrees != 0)
        {
            rotatedBitmap = new SKBitmap(sourceBitmap.Height, sourceBitmap.Width, SKColorType.Gray8, SKAlphaType.Opaque);
            using var canvas = new SKCanvas(rotatedBitmap);
            canvas.Translate(rotatedBitmap.Width, 0);
            canvas.RotateDegrees(degrees);
            canvas.DrawBitmap(sourceBitmap, 0, 0);
        }
        else
            rotatedBitmap = sourceBitmap;

        using var image = SKImage.FromBitmap(rotatedBitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        data.SaveTo(outputStream);
    }
    #endregion
    #region IQueryAttributable Implementation
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        replacementImageStream = query.TryGetValue("ImageStream", out object? streamObject) ? streamObject as Stream : null; // Comes from the camera page
        browsedPictureName = query.TryGetValue("Browsed", out object? browsedObject) ? browsedObject as string : null; // From a browse initiated by the camera page
        startWithCamera = query.TryGetValue("StartWithCamera", out object? startWithCameraObject) && startWithCameraObject is string s && bool.TryParse(s, out bool b) && b;
    }
    #endregion
}
