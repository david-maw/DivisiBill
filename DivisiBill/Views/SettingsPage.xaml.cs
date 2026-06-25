using DivisiBill.Services;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel svm;
    public SettingsPage()
    {
        InitializeComponent();
        svm = BindingContext is SettingsViewModel vm
            ? vm
            : throw new InvalidOperationException("SettingsPage must have a SettingsViewModel as its BindingContext");
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