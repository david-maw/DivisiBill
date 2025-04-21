using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class VenueEditPage : ContentPage
{
    private bool needLocation;
    private readonly VenueEditViewModel venueEditViewModel;
    public VenueEditPage()
    {
        InitializeComponent();
        BindingContext = venueEditViewModel = new VenueEditViewModel();
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
    }
    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        if (needLocation)
            await App.StopMonitoringLocation();
    }
}
