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

    /// <summary>
    /// The message text to display that will fade away
    /// </summary>
    [ObservableProperty]
    public partial string CameraInfo { get; set; }

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
    [NotifyCanExecuteChangedFor(nameof(ChangeTorchModeCommand))]
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
    internal async Task SetCameraPermissionAsync()
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
            await Utilities.DisplayAlertAsync("Camera Permission", "Fault checking camera permission: " + ex.Message, "cancel");
        }
        finally
        {
            IsBusy = false;
        }
    }
    #region Controlling the torch (which is also the Camera Flash)
    [ObservableProperty]
    public partial bool IsTorchOn { get; set; } = false;

    [RelayCommand(CanExecute = nameof(IsFlashAvailable))]
    private void ChangeTorchMode() => IsTorchOn = !IsTorchOn;
    #endregion
    #region Controlling the Camera Flash
    [ObservableProperty]
    public partial CameraFlashMode FlashMode { get; set; } = CameraFlashMode.Off;

    /// <summary>
    /// A boolean property to indicate whether the selected camera supports flash.
    /// This is used to enable or disable the flash and torch controls in the UI.
    /// </summary>
    public bool IsFlashAvailable { get; set; } = false;

    [ObservableProperty]
    public partial bool IsFlashOn { get; set; } = false;

    [RelayCommand(CanExecute = nameof(IsFlashAvailable))]
    private void ChangeFlashMode()
    {
        FlashMode = FlashMode == CameraFlashMode.Off ? CameraFlashMode.On : CameraFlashMode.Off;
        IsFlashOn = FlashMode == CameraFlashMode.On;
    }

    [ObservableProperty]
    public partial bool IsMsgVisible { get; set; } = false;
    #endregion
    #region Commands
    [RelayCommand(CanExecute = nameof(CanSwitchCamera))]
    public async Task SwitchCamera()
    {
        await SwitchCamera(false);
    }

    public async Task SwitchCamera(bool initialize)
    {
        IsMsgVisible = false;
        if (AvailableCameras is null || AvailableCameras.Count <= 0)
            return;
        if (initialize)
        {
            SelectedCamera = null; // Seems to be necessary to make the new selection work
            await Task.Delay(50); // Allow time for the camera to switch
        }
        if (SelectedCamera is null)
            SelectedCamera = AvailableCameras.FirstOrDefault(c => c.Position == CameraPosition.Rear) ?? AvailableCameras[^1];
        else if (AvailableCameras.Count > 1)
        {
            int currentIndex = AvailableCameras.ToList().IndexOf(SelectedCamera);
            int nextIndex = (currentIndex + 1) % AvailableCameras.Count;
            SelectedCamera = AvailableCameras[nextIndex];
        }
        CameraInfo = SelectedCamera.Name;
        IsMsgVisible = true;
    }

    public bool CanSwitchCamera => AvailableCameras is not null && AvailableCameras.Count > 1;
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
