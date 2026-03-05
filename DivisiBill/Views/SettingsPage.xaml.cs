using DivisiBill.Services;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class SettingsPage : ContentPage
{
    private SettingsViewModel svm = null;
    private MealViewModel mvm = null;
    public SettingsPage() => InitializeComponent();
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
        // Reestablish the MealViewModel in case the current meal changed while we were away
        if (Application.Current.Resources.TryGetValue("MealViewModel", out object mvmObject))
            mvm = mvmObject as MealViewModel;
        MealSection.BindingContext = mvm;
        // establish the SettingsViewModel, only need to do this once
        svm ??= BindingContext as ViewModels.SettingsViewModel;
        svm.OnNavigatedTo();
        base.OnNavigatedTo(args);
    }
}