using DivisiBill.Services;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class SettingsPage : ContentPage
{
    MapPage mapPage = null;
    MealViewModel mvm;
    public SettingsPage()
	{
		InitializeComponent();
	}

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
            mapPage.VenueLocationHasChanged = false;
            await App.SetFakeLocation(mapPage.VenueLocation);
        }
        await App.StartMonitoringLocation(); 
    }

    protected async override void OnDisappearing()
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