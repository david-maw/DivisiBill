using DivisiBill.Models;
using DivisiBill.Services;

using ScrollableObject = DivisiBill.ViewModels.VenueListViewModel.ScrollableObject;

namespace DivisiBill.Views;

public partial class VenueListPage : ContentPage
{
    protected ViewModels.VenueListViewModel context;
    #region Page LifeCycle
    public VenueListPage()
    {
        InitializeComponent();
        context = BindingContext as ViewModels.VenueListViewModel
            ?? throw new InvalidOperationException("BindingContext must be a VenueListViewModel");
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
        Shell.Current.FlyoutBehavior = Shell.Current.Navigation.NavigationStack.Count > 1 // we got here by navigation
            ? FlyoutBehavior.Disabled
            : FlyoutBehavior.Flyout;
        await context.OnNavigatedTo();
    }
    protected override async void OnDisappearing()
    {
        Utilities.DebugMsg($"Enter VenueListPage.OnDisappearing, stack depth = {Shell.Current.Navigation.NavigationStack.Count}");
        context.ForgetDeletedVenues();
        if (!Venue.IsSaved)
            await Venue.SaveSettingsAsync();
        base.OnDisappearing();
        await App.StopMonitoringLocation();
        Utilities.DebugMsg($"Leave VenueListPage.OnDisappearing");
    }
    #endregion
    #region Collection Scrolling
    private void ScrollItemsTo(ScrollableObject obj, ScrollToPosition position)
    {
        switch (obj)
        {
            case Venue venue:
                int index = context.VenueList.IndexOf(venue);
                if (index >= 0)
                    CurrentCollectionView.ScrollTo(index, position: position);
                break;
            case int i:
                CurrentCollectionView.ScrollTo(i, position: position);
                break;
        }
    }

    private void OnCollectionViewScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        context.FirstVisibleItemIndex = e.FirstVisibleItemIndex;
        context.LastVisibleItemIndex = e.LastVisibleItemIndex;
    }
    #endregion
}