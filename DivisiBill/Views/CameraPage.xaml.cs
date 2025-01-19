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
        viewModel = BindingContext as CameraViewModel;
    }
    ~CameraPage()
    {
        // For debug of lifetime
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel?.SetCameraAvailabilityAsync();
    }
    private void OnPictureTaken(object sender, CommunityToolkit.Maui.Views.MediaCapturedEventArgs e)
    {
        async void DoIt()
        {
            var navigationParameter = new ShellNavigationQueryParameters
                {
                    { "ImageStream", e.Media}
                };
            // Just exit back to the caller (an ImagePage)
            await App.PushAsync($"..", navigationParameter);
        }

        if (Dispatcher.IsDispatchRequired)
            Dispatcher.Dispatch(() => DoIt());
        else
            DoIt();
    }
    private async void OnTakePicture(object sender, EventArgs e)
    {
        if (cameraProvider?.AvailableCameras is not null)
        {
            var resolutions = cameraProvider.AvailableCameras[0].SupportedResolutions.OrderByDescending(res => res.Width).ThenByDescending(res => res.Height).ToArray();
            if (resolutions.Length > 0)
            {
                var resolution = resolutions.FirstOrDefault(size => size.Width <= 1500 || size.Height <= 480);
                if (resolution.IsZero)
                    resolution = resolutions.First();
                MyCamera.ImageCaptureResolution = resolution;
            }
        }
        await MyCamera.CaptureImage(CancellationToken.None);
    }
}