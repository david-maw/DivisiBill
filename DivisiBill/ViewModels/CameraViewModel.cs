using CommunityToolkit.Maui.Core;
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
    [ObservableProperty]
    public partial bool IsCameraAvailable { get; set; }

    private IReadOnlyList<CameraInfo>? availableCameras;
    public IReadOnlyList<CameraInfo>? AvailableCameras
    {
        get => availableCameras;
        set
        {
            if (SetProperty(ref availableCameras, value))
            {
                SwitchCameraCommand.NotifyCanExecuteChanged();
            }
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeFlashModeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeLightModeCommand))]
    public partial CameraInfo? SelectedCamera { get; set; } = null;
    partial void OnSelectedCameraChanged(CameraInfo? value)
    {
        if (value is not null)
        {
            IsFlashAvailable = value.IsFlashSupported;
            var resolutions = value.SupportedResolutions.OrderByDescending(res => res.Width).ThenByDescending(res => res.Height).ToArray();
            if (resolutions.Length > 0)
            {
                Size resolution = resolutions.FirstOrDefault(size => size.Width <= 1500 || size.Height <= 480);
                if (resolution.IsZero)
                    resolution = resolutions.First();
                ImageCaptureResolution = resolution;
            }
        }
        else
        {
            ImageCaptureResolution = Size.Zero;
            IsFlashAvailable = false;
        }
    }
    [ObservableProperty]
    public partial Size ImageCaptureResolution { get; set; }
    public CancellationToken Token => IsCameraAvailable ? CancellationToken.None : CancellationToken.None;
    #endregion
    internal async Task SetCameraAvailabilityAsync()
    {
        try
        {
            IsBusy = true;
            PermissionStatus status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.Camera>();
            }

            IsCameraAvailable = status == PermissionStatus.Granted && MediaPicker.IsCaptureSupported;
        }
        catch (Exception ex)
        {
            IsCameraAvailable = false;
            await Utilities.DisplayAlertAsync("Camera Availability", "Could not check camera availability: " + ex.Message, "cancel");
        }
        finally
        {
            IsBusy = false;
        }
    }
    #region Controlling the light (which is also the Camera Flash)
    [ObservableProperty]
    public partial bool IsLightOn { get; set; } = false;

    [RelayCommand(CanExecute = nameof(IsFlashAvailable))]
    private void ChangeLightMode() => IsLightOn = !IsLightOn;
    #endregion
    #region Controlling the Camera Flash
    [ObservableProperty]
    public partial CameraFlashMode FlashMode { get; set; } = CameraFlashMode.Off;

    public bool IsFlashAvailable { get; set; } = false;

    [ObservableProperty]
    public partial bool IsFlashOn { get; set; } = false;

    [RelayCommand(CanExecute = nameof(IsFlashAvailable))]
    private void ChangeFlashMode()
    {
        FlashMode = FlashMode == CameraFlashMode.Off ? CameraFlashMode.On : CameraFlashMode.Off;
        IsFlashOn = FlashMode == CameraFlashMode.On;
    }

    [RelayCommand(CanExecute = nameof(CanSwitchCamera))]
    private void SwitchCamera()
    {
        if (AvailableCameras is null || AvailableCameras.Count <= 1 || SelectedCamera is null)
            return;

        int currentIndex = AvailableCameras.ToList().IndexOf(SelectedCamera);
        int nextIndex = (currentIndex + 1) % AvailableCameras.Count;
        SelectedCamera = AvailableCameras[nextIndex];
    }

    private bool CanSwitchCamera() => AvailableCameras is not null && AvailableCameras.Count > 1;
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
            FileResult? photo = (await MediaPicker.PickPhotosAsync()).FirstOrDefault();
            // We have identified an  image, now copy it to the private storage area, so we have it later, if it is needed
            if (photo is not null)
            {
                ShellNavigationQueryParameters navigationParameter = new()
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
