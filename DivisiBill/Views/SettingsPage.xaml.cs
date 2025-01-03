using DivisiBill.Services;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class SettingsPage : ContentPage
{
    private MapPage mapPage = null;
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
        if (mapPage is not null && mapPage.VenueLocationHasChanged)
        {
            bool locationChanged = App.MyLocation is not null;
            mapPage.VenueLocationHasChanged = false;
            if (mapPage.VenueLocation is not null && locationChanged)
            {
                await CommunityToolkit.Maui.Alerts.Toast.Make("Will set fake location in 10s").Show();
                await Task.Delay(10_000);
            }
            await App.SetFakeLocation(mapPage.VenueLocation);
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
        mapPage ??= new MapPage();
        mapPage.VenueName = "Home";
        mapPage.VenueLocation = App.MyLocation;
        mapPage.VenueLocationHasChanged = false;
        await Navigation.PushAsync(mapPage);
    }
}