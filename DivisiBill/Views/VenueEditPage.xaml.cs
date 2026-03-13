using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class VenueEditPage : ContentPage
{
    private bool needLocation;
    private readonly VenueEditViewModel venueEditViewModel;
    public VenueEditPage(Services.PlacesService placesService)
    {
        InitializeComponent();
        BindingContext = venueEditViewModel = new VenueEditViewModel(placesService);
    }
    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        venueEditViewModel.OnNavigatedTo();
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

    /// <summary>
    /// Guarantee we can see the start of the text when the user focuses on an entry that already has text in it.
    /// This is especially important for the name entry, since the name is used as the title of the page and if
    /// the cursor is at the end of the text, the user won't be able to see what venue they are editing.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void SelectFirst(object sender, FocusEventArgs e)
    {
        if (sender is Entry entry && !string.IsNullOrEmpty(entry.Text))
        {
            entry.CursorPosition = 0;
            entry.SelectionLength = 0;
        }
    }
}
