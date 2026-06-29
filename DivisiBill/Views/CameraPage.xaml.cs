using CommunityToolkit.Maui.Core;
using DivisiBill.Services;
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
    ~CameraPage()
    {
        // For debug of lifetime
        Utilities.DebugMsg("In ~CameraPage");
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.SetCameraAvailabilityAsync();

        // Initialize available cameras - wait briefly for the camera provider to enumerate cameras
        if (viewModel.IsCameraAvailable)
        {
            // Give the CameraView a moment to initialize cameras
            // This is necessary because AvailableCameras may be null on first access
            await Task.Delay(100);

            viewModel.AvailableCameras = cameraProvider.AvailableCameras;

            // If still null after the delay, try again after another brief wait
            if (viewModel.AvailableCameras is null)
            {
                await Task.Delay(200);
                viewModel.AvailableCameras = cameraProvider.AvailableCameras;
            }

            // Select the rear camera by default if available, otherwise the first camera
            if (viewModel.AvailableCameras is not null && viewModel.AvailableCameras.Count > 0)
            {
                viewModel.SelectedCamera = viewModel.AvailableCameras.FirstOrDefault(c => c.Position == CameraPosition.Rear)
                                        ?? viewModel.AvailableCameras[0];
            }
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