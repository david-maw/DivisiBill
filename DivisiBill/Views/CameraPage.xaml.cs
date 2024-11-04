using CommunityToolkit.Maui.Core;
using System.Runtime.Versioning;

namespace DivisiBill.Views;

// Inhibit warnings
[SupportedOSPlatform("windows10.0.10240.0")]
[SupportedOSPlatform("android21.1")]
public partial class CameraPage : ContentPage
{
    private readonly ICameraProvider cameraProvider;
    public CameraPage(ICameraProvider cameraProviderParam)
    {
        cameraProvider = cameraProviderParam;
        InitializeComponent();
    }
    ~CameraPage()
    {
        // For debug of lifetime
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