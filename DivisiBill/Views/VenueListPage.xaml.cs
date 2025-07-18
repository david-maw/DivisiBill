#nullable enable

using DivisiBill.Models;
using DivisiBill.Services;

namespace DivisiBill.Views;

public partial class VenueListPage : ContentPage
{
    protected ViewModels.VenueListViewModel context;
    private MapSettings? mapSettings = null;

    public VenueListPage()
    {
        InitializeComponent();
        context = new ViewModels.VenueListViewModel(
            NavigateToDetails: async (v) => await App.PushAsync(Routes.VenueEditPage, "Venue", v),
            NavigateToHome: async () => { await App.GoToHomeAsync(); });
        BindingContext = context;
        context.ScrollItemsTo = ScrollItemsTo;
    }
    ~VenueListPage() { context.ScrollItemsTo = null; }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await App.StartMonitoringLocation();
    }
    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        await Task.CompletedTask; // Just to avoid warning about async with no await on a Windows build
        Shell.Current.FlyoutBehavior = Shell.Current.Navigation.NavigationStack.Count > 1 // we got here by navigation
            ? FlyoutBehavior.Disabled
            : FlyoutBehavior.Flyout;
        if (mapSettings is not null && mapSettings.VenueLocationHasChanged)
        {
            mapSettings.VenueLocationHasChanged = false; // don't execute this code again unnecessarily
            Venue v = Venue.FindVenueByName(mapSettings.VenueName);
            v?.Location = mapSettings.VenueLocation;
        }
        if (context.CurrentItem is null)
        {
            // For convenience, select the venue for the current meal if it exists
            if (!string.IsNullOrWhiteSpace(Meal.CurrentMeal.VenueName))
            {
                context.CurrentItem = Venue.FindVenueByName(Meal.CurrentMeal.VenueName);
                if (context.CurrentItem is not null)
                    CurrentCollectionView.ScrollTo(context.CurrentItem);
            }
        }
        context.ShowVenuesHint = App.Settings.ShowVenuesHint;

        // TODO This causes crashes on Windows as of .NET 8 rc2 - restore it when that's fixed, see https://github.com/dotnet/maui/issues/18530
#if ANDROID
        await Task.Delay(200); // Without the delay the scroll doesn't work

        try { CurrentCollectionView.ScrollTo(context.CurrentItem); }
        catch (Exception) { } // Don't care if the selection fails
#endif
    }
    protected override async void OnDisappearing()
    {
        context.ForgetDeletedVenues();
        if (!Venue.IsSaved)
            await Venue.SaveSettingsAsync();
        base.OnDisappearing();
        await App.StopMonitoringLocation();
    }

    private async void OnShowMap(object sender, EventArgs e)
    {
        if (Utilities.IsWinUI)
        {
            await Utilities.ShowAppSnackBarAsync("Map is not available on Windows");
            return;
        }
        Venue? v = ((BindableObject)sender).BindingContext as Venue ?? context.CurrentItem;
        if (v is not null)
        {
            mapSettings = new(v.Name, v.Location);
            if (!Utilities.IsWinUI || App.BingMapsAllowed)
                await App.PushAsync(Routes.MapPage, "MapSettings", mapSettings);
        }
    }
    #region Collection Scrolling
    private void ScrollItemsTo(int index, bool toEnd) // Passed in to viewModel
        => CurrentCollectionView.ScrollTo(index, position: toEnd ? ScrollToPosition.End : ScrollToPosition.Start);
    private void OnCollectionViewScrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        context.FirstVisibleItemIndex = e.FirstVisibleItemIndex;
        context.LastVisibleItemIndex = e.LastVisibleItemIndex;
    }
    #endregion
}