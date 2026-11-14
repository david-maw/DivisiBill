using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class ImagePage : ContentPage
{
    private readonly ImageViewModel viewModel;
    private bool isFlyoutBehaviorOverridden;

    public ImagePage()
    {
        InitializeComponent();
        viewModel = (ImageViewModel)BindingContext;
    }
    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        isFlyoutBehaviorOverridden = false;
        if (Shell.Current.Navigation.NavigationStack.Count > 1
            && Shell.Current.FlyoutBehavior is not FlyoutBehavior.Disabled) // we got here by push navigation
        {
            Shell.Current.FlyoutBehavior = FlyoutBehavior.Disabled;
            isFlyoutBehaviorOverridden = true;
        }
        await viewModel.OnNavigatedTo();
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        if (isFlyoutBehaviorOverridden)
        {
            Shell.Current.ClearValue(Shell.FlyoutBehaviorProperty); // so undo the fact we set it explicitly
            isFlyoutBehaviorOverridden = false;
        }
    }

    // OnNavigatedTo is not called if navigation is to ".." so do not rely on it 
    protected override void OnDisappearing()
    {
        viewModel.Store();
        base.OnDisappearing();
    }
}

