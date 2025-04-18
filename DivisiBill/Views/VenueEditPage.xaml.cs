using DivisiBill.Services;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class VenueEditPage : ContentPage
{
    private bool needLocation;
    private readonly VenueEditViewModel venueEditViewModel;
    private readonly MapPage mapPage = new();
    public VenueEditPage()
    {
        InitializeComponent();
        BindingContext = venueEditViewModel = new VenueEditViewModel(async (v) =>
        {
            mapPage.VenueName = venueEditViewModel.Name;
            mapPage.VenueLocation = venueEditViewModel.Location;
            mapPage.VenueLocationHasChanged = false;
            if (!Utilities.IsUWP || App.BingMapsAllowed)
                await Navigation.PushAsync(mapPage);
        });
    }
    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        venueEditViewModel.Initialize();
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        IsEnabled = false; // Kludge to close keyboard if it's open
        IsEnabled = true;  // when we switch to the Map page
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await App.InitializationComplete.Task;
        needLocation = App.UseLocation && venueEditViewModel.Location is not null; // Current location is needed for correct display of distance
        if (needLocation)
            await App.StartMonitoringLocation();
        if (mapPage?.VenueLocationHasChanged == true)
        {
            venueEditViewModel.Location = mapPage.VenueLocation;
            mapPage.VenueLocationHasChanged = false;
        }
    }
    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        if (needLocation)
            await App.StopMonitoringLocation();
    }
}
