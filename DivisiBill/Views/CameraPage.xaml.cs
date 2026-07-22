using CommunityToolkit.Maui.Core;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class CameraPage : ContentPage
{
    private readonly ICameraProvider cameraProvider;
    private readonly CameraViewModel viewModel;
    public CameraPage(ICameraProvider cameraProviderParam)
    {
        cameraProvider = cameraProviderParam;
        InitializeComponent();
        viewModel = BindingContext is CameraViewModel vm
            ? vm
            : throw new InvalidOperationException("CameraPage must have a CameraViewModel as its BindingContext");
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.SetCameraPermissionAsync();

        // Initialize available cameras - wait briefly for the camera provider to enumerate cameras
        if (viewModel.IsCameraAvailable)
        {
            // Give the CameraView a moment to initialize cameras
            // This is necessary because AvailableCameras may be null on first access
            await Task.Delay(100);

            viewModel.AvailableCameras = cameraProvider.AvailableCameras;

            // If still null after the delay, try again after a longer wait
            if (viewModel.AvailableCameras is null)
            {
                await Task.Delay(500);
                viewModel.AvailableCameras = cameraProvider.AvailableCameras;
            }

            // Select the default camera
            await viewModel.SwitchCamera(initialize: true);
        }
    }
    private void OnPictureTaken(object? sender, MediaCapturedEventArgs e)
    {
        async void DoIt()
        {
            ShellNavigationQueryParameters navigationParameter = new()
            {
                    { "ImageStream", e.Media}
                };
            // Just exit back to the caller (an ImagePage)
            await App.PushAsync($"..", navigationParameter);
        }

        if (Dispatcher.IsDispatchRequired)
            Dispatcher.Dispatch(DoIt);
        else
            DoIt();
    }
}