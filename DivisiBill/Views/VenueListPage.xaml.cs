using DivisiBill.Models;
using DivisiBill.Services;

using ScrollToTarget = DivisiBill.ViewModels.VenueListViewModel.ScrollToTarget;

namespace DivisiBill.Views;

public partial class VenueListPage : ContentPage
{
    protected ViewModels.VenueListViewModel context;
    private bool isFlyoutBehaviorOverridden;

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
        isFlyoutBehaviorOverridden = false;
        if (Shell.Current.Navigation.NavigationStack.Count > 1
            && Shell.Current.FlyoutBehavior is not FlyoutBehavior.Disabled) // we got here by push navigation
        {
            Shell.Current.FlyoutBehavior = FlyoutBehavior.Disabled;
            isFlyoutBehaviorOverridden = true;
        }
        await context.OnNavigatedTo();
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        if (isFlyoutBehaviorOverridden)
        {
            Shell.Current.ClearValue(Shell.FlyoutBehaviorProperty); // so undo the fact we set it explicitly
            isFlyoutBehaviorOverridden = false;
        }
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
    private void ScrollItemsTo(ScrollToTarget obj, ScrollToPosition position)
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