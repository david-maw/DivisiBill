using DivisiBill.Services;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class SettingsPage : ContentPage
{
    private SettingsViewModel svm = null;
    public SettingsPage()
    {
        InitializeComponent();
        svm = BindingContext as ViewModels.SettingsViewModel;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await App.StartMonitoringLocation();
    }
    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await App.StopMonitoringLocation();
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        Utilities.DebugMsg($"Entering {nameof(SettingsPage)}.{nameof(OnNavigatedTo)}");
        svm.OnNavigatedTo();
        base.OnNavigatedTo(args);
    }
}