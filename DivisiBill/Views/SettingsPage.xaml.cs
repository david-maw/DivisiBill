using DivisiBill.Services;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class SettingsPage : ContentPage
{
    private MapSettings mapSettings;
    private MealViewModel mvm;
    public SettingsPage() => InitializeComponent();
    protected override async void OnAppearing()
    {
        Utilities.DebugMsg("In OnAppearing, perhaps returning from modifying subscription");
        if (Application.Current.Resources.TryGetValue("MealViewModel", out object mvmObject))
            mvm = mvmObject as MealViewModel;
        MealSection.BindingContext = mvm;
        mvm.LoadSettings();
        base.OnAppearing();
        var svm = BindingContext as ViewModels.SettingsViewModel;
        svm.RefreshValues();
        if (mapSettings is not null && mapSettings.VenueLocationHasChanged)
        {
            bool locationChanged = App.MyLocation is not null;
            if (mapSettings.VenueLocation is not null && locationChanged)
            {
                await Utilities.ShowAppSnackBarAsync("Will set fake location in 10s");
                await Task.Delay(10_000);
            }
            await App.SetFakeLocation(mapSettings.VenueLocation);
            mapSettings.VenueLocationHasChanged = false; // So we do not reuse it accidentally
        }
        await App.StartMonitoringLocation();
    }
    protected override async void OnDisappearing()
    {
        if (IsEnabled)
        {
            mvm.UnloadSettings();
            await App.StopMonitoringLocation();
            base.OnDisappearing();
        }
    }
    private async void OnSetLocation(object sender, EventArgs e)
    {
        mapSettings = new("Home", App.MyLocation);
        await App.PushAsync(Routes.MapPage, "MapSettings", mapSettings);
    }
}