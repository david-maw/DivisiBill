using CommunityToolkit.Maui.Core.Primitives;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Services;

namespace DivisiBill.ViewModels;

public partial class CameraViewModel : ObservableObject
{
    #region Initialization and State
    /// <summary>
    /// Handy boolean property to describe when asynchronous work is in process
    /// </summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }
    #endregion
    #region Controlling the light (which is also the Camera Flash)
    /// <summary>
    /// The glyph to use for the flash command - note it is inverted because it is showing what the glyph will do, not what the current state is
    /// Because this doesn't seem to work on android there's no UI for it
    /// </summary>
    [ObservableProperty]
    public partial FontImageSource LightGlyph { get; set; } = (FontImageSource)Application.Current.Resources["GlyphFlashlightOn"];

    [ObservableProperty]
    public partial bool IsLightOn { get; set; } = false;

    partial void OnIsLightOnChanged(bool value)
    {
        LightGlyph = (FontImageSource)(value ? Application.Current.Resources["GlyphFlashlightOff"] : Application.Current.Resources["GlyphFlashlightOn"]);
    }
    [RelayCommand]
    private void ChangeLightMode()
    {
        IsLightOn = !IsLightOn;
    }
    #endregion
    #region Controlling the Camera Flash
    /// <summary>
    /// The glyph to use for the flash command - note it is inverted because it is showing what the glyph will do, not what the current state is
    /// </summary>
    [ObservableProperty]
    public partial FontImageSource FlashGlyph { get; set; } = (FontImageSource)Application.Current.Resources["GlyphFlashOn"];

    [ObservableProperty]
    private CameraFlashMode flashMode = CameraFlashMode.Off;

    [RelayCommand]
    private void ChangeFlashMode()
    {
        FlashMode = FlashMode == CameraFlashMode.Off ? CameraFlashMode.On : CameraFlashMode.Off;
        FlashGlyph = (FontImageSource)(FlashMode == CameraFlashMode.Off ? Application.Current.Resources["GlyphFlashOn"] : Application.Current.Resources["GlyphFlashOff"]);
    }
    #endregion
    #region Commands
    /// <summary>
    /// Initiate a UI to allow the user to browse existing images for a suitable bill image. If one is selected
    /// return its data stream to the calling page <see cref="ViewModels.ImageViewModel"/> and <see cref="ImageViewModel"/>
    /// </summary>
    [RelayCommand]
    private async Task Browse()
    {
        try
        {
            IsBusy = true;
            var photo = await MediaPicker.PickPhotoAsync();
            // We have identified an  image, now copy it to the private storage area, so we have it later, if it is needed
            if (photo is not null)
            {
                var navigationParameter = new ShellNavigationQueryParameters
                {
                    { "Browsed", photo.FileName},
                    { "ImageStream", await photo.OpenReadAsync()}
                };

                await App.PushAsync("..", navigationParameter);
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
    #endregion
}
