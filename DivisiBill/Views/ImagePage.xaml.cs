using DivisiBill.Services;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;
public partial class ImagePage : ContentPage
{
    private readonly ImageViewModel viewModel;
    public ImagePage()
    {
        InitializeComponent();
        viewModel = (ImageViewModel)BindingContext;
    }
    private FlyoutBehavior savedFlyoutBehavior;
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        savedFlyoutBehavior = Shell.Current.FlyoutBehavior;
        Shell.Current.FlyoutBehavior = Shell.Current.Navigation.NavigationStack.Count > 1 // we got here by navigation
            ? FlyoutBehavior.Disabled
            : FlyoutBehavior.Flyout;
        if (!(Utilities.IsDebug || await Flashlight.IsSupportedAsync()))
            ToolbarItems.Remove(FlashlightTbi); // Don't bother to display an ineffective icon in a release build
        await viewModel.ProcessQueryAsync();
    }
    // OnNavigatedTo is not called if navigation is to ".." so do not rely on it 
    protected override void OnDisappearing()
    {
        viewModel.Store();
        base.OnDisappearing();
        Shell.Current.FlyoutBehavior = savedFlyoutBehavior;
    }
}

