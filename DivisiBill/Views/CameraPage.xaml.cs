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
        viewModel = BindingContext as CameraViewModel;
    }
    ~CameraPage()
    {
        // For debug of lifetime
        Utilities.DebugMsg("In ~CameraPage");
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel?.SetCameraAvailabilityAsync();
    }
    private void OnPictureTaken(object sender, MediaCapturedEventArgs e)
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