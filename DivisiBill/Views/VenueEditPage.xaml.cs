using DivisiBill.Models;
using DivisiBill.Services;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class VenueEditPage : ContentPage
{
    private bool needLocation;
    private VenueEditViewModel venueEditViewModel;
    private MapPage mapPage = new MapPage();

    public VenueEditPage()
    {
        InitializeComponent();
    }
    public VenueEditPage(Venue venueParam) : this()
    {
        BindingContext = venueEditViewModel = new VenueEditViewModel(venueParam, async () => await Navigation.PopAsync(), async (v) =>
        {
            IsEnabled = false;
            IsEnabled = true;   // Kludge to close keyboard if it's open
            mapPage.VenueName = venueEditViewModel.Name;
            mapPage.VenueLocation = venueEditViewModel.MyLocation;
            mapPage.VenueLocationHasChanged = false;
            if (!Utilities.IsUWP || App.BingMapsAllowed)
                await Navigation.PushAsync(mapPage);
        });
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await App.InitializationComplete.Task;
        needLocation = App.UseLocation && venueEditViewModel.MyLocation is not null; // Current location is needed for correct display of distance
        if (needLocation)
            await App.StartMonitoringLocation();
        if (mapPage?.VenueLocationHasChanged != false)
        {
            venueEditViewModel.MyLocation = mapPage.VenueLocation;
            mapPage.VenueLocationHasChanged = false;
        }
    }
    protected async override void OnDisappearing()
    {
        base.OnDisappearing();
        if (needLocation)
            await App.StopMonitoringLocation();
    }
}
