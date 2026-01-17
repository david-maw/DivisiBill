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
    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
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
    }
}

