#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Models;
using DivisiBill.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace DivisiBill.ViewModels;

public partial class VenueListViewModel : ObservableObjectPlus
{
    #region Initialization
    #region UI Parameters
    private readonly Action<Venue> NavigateToDetails;
    private readonly Action NavigateToHome;
    #endregion
    public VenueListViewModel(Action<Venue> NavigateToDetails, Action NavigateToHome)
    {
        ((INotifyCollectionChanged)Venue.AllVenues).CollectionChanged += AllVenues_CollectionChanged;
        App.MyLocationChanged += App_MyLocationChanged;
        this.NavigateToDetails = NavigateToDetails;
        this.NavigateToHome = NavigateToHome;
    }
    ~VenueListViewModel()
    {
        ((INotifyCollectionChanged)Venue.AllVenues).CollectionChanged -= AllVenues_CollectionChanged;
        App.MyLocationChanged -= App_MyLocationChanged;
    }
    private void AllVenues_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => OnPropertyChanged(nameof(VenueCount));
    #endregion
    #region General Commands
    [RelayCommand]
    private void Sort() => NextSortOrder();

    [RelayCommand]
    private void Add()
    {
        CurrentItem = Venue.SelectOrAddVenue("New");
        NavigateToDetails(CurrentItem);
    }

    [RelayCommand]
    private async Task AssignAsync(Venue venueParam)
    {
        Venue? v = venueParam ?? CurrentItem;
        if (v is not null)
        {
            await Meal.CurrentMeal.ChangeVenueAsync(v.Name);
            if (App.UseLocation)
                v.SetLocationIfBetter(App.MyLocation);
            NavigateToHome();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync(Venue v) => await DeleteVenueAsync(v);
    private bool CanDelete(Venue v) => v is null || string.IsNullOrWhiteSpace(Meal.CurrentMeal?.VenueName) || !Meal.CurrentMeal.VenueName.Equals(v?.Name, StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void ShowDetails(Venue venueParam)
    {
        Venue? v = venueParam ?? CurrentItem;
        if (v is not null)
        {
            CurrentItem = v;
            NavigateToDetails(v);
        }
    }

    [RelayCommand]
    private async Task UnDeleteAllVenues() => await UndeleteAllVenuesAsync();
    #endregion
    #region Properties
    [ObservableProperty]
    public partial Venue? CurrentItem { get; set; }

#if WINDOWS
    private Venue? lastVenueSelectedByMe = null;
#endif

    [RelayCommand]
    public void SelectVenue(Venue venueParam)
    {
#if WINDOWS
        // Unfortunately Windows selects any new item before calling this code
        // probably related to https://github.com/dotnet/maui/issues/5446
        // This kludge works around that as long as you only use this method for selection
        if (venueParam == lastVenueSelectedByMe)
        {
            CurrentItem = null;
            lastVenueSelectedByMe = null;
        }
        else
        {
            CurrentItem = venueParam;
            lastVenueSelectedByMe = venueParam;
        }
#else        
        if (venueParam == CurrentItem)
            CurrentItem = null;
        else if (venueParam is not null)
            CurrentItem = venueParam;
#endif
    }
    public bool ShowVenuesHint
    {
        get;
        set => SetProperty(ref field, value, () => App.Settings.ShowVenuesHint = value);
    } = false;
    #endregion
    #region Venues
    [RelayCommand]
    public async Task GetRemoteVenueListAsync()
    {
        if (App.IsCloudAllowed)
        {
            var fileListViewModel = new FileListViewModel(RemoteWs.VenueListTypeName);
            await fileListViewModel.InitializeAsync();
            if (fileListViewModel.FileListCount > 0)
            {
                await Shell.Current.Navigation.PushAsync(new Views.FileListPage(fileListViewModel));
                var result = await fileListViewModel.SelectionCompleted.Task;
                if (result is not null)
                {
                    bool loaded = await Venue.LoadFromRemoteAsync(result.Name, result.ReplaceRequested);
                    if (!loaded)
                        await Utilities.DisplayAlertAsync("Error", "No remote list found");
                }
            }
            else
                await Utilities.DisplayAlertAsync("Error", "No lists were found");
        }
        else if (!App.Settings.IsCloudAccessAllowed)
            await Utilities.DisplayAlertAsync("Error", "Cloud access is not enabled in program settings");
        else
            await Utilities.DisplayAlertAsync("Error", "Cloud is enabled but currently inaccessible");
    }
    #region Venue Delete/Undelete
    private readonly Stack<Venue> deletedVenues = new();

    [RelayCommand]
    public async Task UndeleteVenueAsync()
    {
        if (IsAnyDeletedVenue)
        {
            deletedVenues.Pop().InsertInVenueLists();
            IsAnyDeletedVenue = deletedVenues.Count != 0;
            await Venue.SaveSettingsAsync();
        }
    }

    [RelayCommand]
    public async Task UndeleteAllVenuesAsync()
    {
        if (IsAnyDeletedVenue)
        {
            while (deletedVenues.Count != 0)
                await UndeleteVenueAsync();
        }
    }
    public void ForgetDeletedVenues()
    {
        deletedVenues.Clear();
        IsAnyDeletedVenue = false;
    }
    public bool IsAnyDeletedVenue
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(IsAnyDeletedVenue));
            }
            // Always recheck IsManyDeletedVenues because for it transitions between {0,1} and {2+} are what count 
            OnPropertyChanged(nameof(IsManyDeletedVenues));
        }
    } = false;
    public bool IsManyDeletedVenues => deletedVenues.Count > 1;
    private async Task DeleteVenueAsync(Venue venueParam)
    {
        Venue? v = venueParam ?? CurrentItem;
        if (v is not null)
        {
            if (v == CurrentItem)
                CurrentItem = VenueList.Alternate(CurrentItem);
            deletedVenues.Push(v);
            IsAnyDeletedVenue = true;
            v.Forget();
            _ = Venue.SaveSettingsAsync();
            var mealsForVenue = Meal.LocalMealList.Where((ms) => ms.IsLocal && ms.VenueName == v.Name);
            if (mealsForVenue.Any() && await Utilities.AskAsync("Question", "Do you want to delete local bills for " + v.Name))
            {
                foreach (MealSummary sum in mealsForVenue.OrderBy((ms) => ms.CreationTime))
                    await sum.DeleteAsync(doLocal: true, doRemote: false);
            }
        }
    }
    #endregion
    public int VenueCount => Venue.AllVenues.Count;
    private async void App_MyLocationChanged(object? sender, EventArgs e) => await Venue.UpdateAllDistances();
    public ObservableCollection<Venue> VenueList => (SortOrder == SortOrderType.byName) ? Venue.AllVenues : Venue.AllVenuesByDistance;
    #endregion
    #region Sorting
    public string SortOrderName => SortOrder == SortOrderType.byName ? "Name" : SortOrder == SortOrderType.byDistance ? "Distance" : "Unknown";

    public enum SortOrderType { byDistance, byName };
    private void NextSortOrder()
    {
        if (SortOrder == Enum.GetValues<SortOrderType>().Max())
            SortOrder = Enum.GetValues<SortOrderType>().Min();
        else
            SortOrder++;
    }

    public SortOrderType SortOrder
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(SortOrderName));
                OnPropertyChanged(nameof(VenueList));
            }
        }
    } = SortOrderType.byName;
    #endregion
    #region Scrolling Item list
    [ObservableProperty]
    public partial bool IsSwipeUpAllowed { get; set; }

    [ObservableProperty]
    public partial bool IsSwipeDownAllowed { get; set; }

    [ObservableProperty]
    public partial int FirstVisibleItemIndex { get; set; }

    partial void OnFirstVisibleItemIndexChanged(int value) => IsSwipeDownAllowed = value > 0;

    [ObservableProperty]
    public partial int LastVisibleItemIndex { get; set; }

    partial void OnLastVisibleItemIndexChanged(int value) => IsSwipeUpAllowed = value > 0 && value < VenueList.Count - 1;

    public Action<int, bool>? ScrollItemsTo = null;

    [RelayCommand]
    private void ScrollItems(string whereTo)
    {
        if (FirstVisibleItemIndex == LastVisibleItemIndex || ScrollItemsTo is null || VenueList is null)
            return;
        int lastItemIndex = VenueList.Count - 1;
        if (lastItemIndex < 2)
            return;
        try
        {
            switch (whereTo)
            {
                case "Up": if (LastVisibleItemIndex < lastItemIndex) ScrollItemsTo(LastVisibleItemIndex, false); break;
                case "Down": if (FirstVisibleItemIndex > 0) ScrollItemsTo(FirstVisibleItemIndex, true); break;
                case "End": if (LastVisibleItemIndex < lastItemIndex) ScrollItemsTo(lastItemIndex, false); break;
                case "Start": if (FirstVisibleItemIndex > 0) ScrollItemsTo(0, true); break;
                default: break;
            }
        }
        catch (Exception ex)
        {
            ex.ReportCrash("fault attempting to scroll");
            // Do nothing, we do not really care if a scroll attempt fails
        }
    }
    #endregion
}
