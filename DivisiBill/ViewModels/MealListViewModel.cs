using DivisiBill.Models;
using DivisiBill.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DivisiBill.ViewModels;

/// <summary>
/// This manages the list of bills sorted and filtered in various ways. The contents of the list (MealList) is composed of the same set of MealSummaries that other instances see so setting some
/// property on one propagates to other views.
/// </summary>
[QueryProperty(nameof(Sort), "sort")]
[QueryProperty(nameof(IsSelectableList), "IsSelectableList")]
[QueryProperty(nameof(SetCount), "count")]
[QueryProperty(nameof(ShowLocalMeals), "ShowLocal")]
[QueryProperty(nameof(ShowRemoteMeals), "ShowRemote")]
public partial class MealListViewModel : ObservableObjectPlus
{
    private Func<MealSummary, Task> showDetailsParam;
    private Func<MealSummary, Task> useMealParam;
    public Func<MealSummary, Task> UseMealParam { get => useMealParam; set => useMealParam = value; }
    public Func<MealSummary, Task> ShowDetailsParam { get => showDetailsParam; set => showDetailsParam = value; }

    public MealListViewModel()
    {
        Meal.LocalMealList.CollectionChanged += LocalMealList_CollectionChanged;
        Meal.RemoteMealList.CollectionChanged += RemoteMealList_CollectionChanged;
        App.MyLocationChanged += App_MyLocationChanged;
    }
    private void App_MyLocationChanged(object sender, EventArgs e)
    {
        if (sortOrder == SortOrderType.byDistance)
            InvalidateMealList();
    }
    ~MealListViewModel() 
    {
        Meal.LocalMealList.CollectionChanged -= LocalMealList_CollectionChanged;
        Meal.RemoteMealList.CollectionChanged -= RemoteMealList_CollectionChanged;
        App.MyLocationChanged -= App_MyLocationChanged;
    }

    public async Task OnAppearing()
    {
        CheckDeleted();
        await App.StartMonitoringLocation();
        SetSelectedMealSummariesCount(); // Just in case another page changed it
        IsCloudAllowed = App.IsCloudAllowed;
    }
    public async Task OnDisappearing()
    {
        ForgetDeleted();
        await App.StopMonitoringLocation();
    }

    /// <summary>
    /// Called whenever the local Meal list changes, which can happen if asynchronous restore or recover operations are in process
    /// or if asynchronous file cleanup is in process. Its basic job is to keep the displayed list in sync with the changes if necessary.
    /// </summary>
    private void LocalMealList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (!ShowLocalMeals) return;
        if (e.Action == NotifyCollectionChangedAction.Remove && mealList is not null)
            foreach (MealSummary ms in e.OldItems)
            {
                if (!(ms.IsRemote && ShowRemoteMeals)) // in other words if not already in the list
                    if (mealList.Remove(ms) && ms.FileSelected) // DeselectInvisibleMeals() is not needed here because we handle it directly
                    {
                        ms.FileSelected = false;
                        SelectedMealSummariesCount--;
                    }
            }
        else if (e.Action == NotifyCollectionChangedAction.Add && mealList is not null) // Probably an Undelete operation
            foreach (MealSummary ms in e.NewItems)
            {
                if (!(ms.IsRemote && ShowRemoteMeals)) // in other words if not already in the list
                    if (UpsertIntoMealList(ms) && ms.FileSelected) // DeselectInvisibleMeals() is not needed here because we handle it directly
                        SelectedMealSummariesCount++;
            }
        else
        if (e.Action == NotifyCollectionChangedAction.Reset && mealList is not null)
        {
            foreach (MealSummary ms in MealList.Where(ms => ms.FileSelected && ms.IsLocal))
                ms.FileSelected = false;
            InvalidateMealList();
        }
        else
        {
            InvalidateMealList();
        }
    }
    /// <summary>
    /// Called whenever the remote Meal list changes, which can happen if asynchronous archive or backup operations are in process
    /// or if asynchronous cleanup of the remote list is in process. Its basic job is to keep the displayed list in sync with the changes if necessary.
    /// </summary>
    private void RemoteMealList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (!ShowRemoteMeals) return;
        if (e.Action == NotifyCollectionChangedAction.Remove && mealList is not null)
            foreach (MealSummary ms in e.OldItems)
            {
                if (!(ms.IsLocal && ShowLocalMeals)) // in other words if not already in the list
                    if (mealList.Remove(ms) && ms.FileSelected) // DeselectInvisibleMeals() is not needed here because we handle it directly
                    {
                        ms.FileSelected = false;
                        SelectedMealSummariesCount--;
                    }
            }
        else if (e.Action == NotifyCollectionChangedAction.Add && mealList is not null) // Probably an Undelete operation
            foreach (MealSummary ms in e.NewItems)
            {
                if (!(ms.IsLocal && ShowLocalMeals)) // in other words if not already in the list
                    if (UpsertIntoMealList(ms) && ms.FileSelected) // DeselectInvisibleMeals() is not needed here because we handle it directly
                        SelectedMealSummariesCount++;
            }
        else
        if (e.Action == NotifyCollectionChangedAction.Reset && mealList is not null)
        {
            foreach (MealSummary ms in MealList.Where(ms => ms.FileSelected && ms.IsLocal))
                ms.FileSelected = false;
            InvalidateMealList();
        }
        else
        {
            InvalidateMealList();
        }
    }
    private MealSummary BestMealSummary(MealSummary ms) => ms is not null ? ms : IsSelectableList ? MealList.Where(ms => ms.FileSelected).FirstOrDefault() : SelectedMealSummary;

    /// <summary>
    /// Change between single and multiple selection, preserving the selected items of the "other" state
    /// </summary>
    [RelayCommand]
    private void ChangeList() => IsSelectableList = !IsSelectableList;

    /// <summary>
    /// Call the passed-in ShowDetails function to show details of this MealSummary to the user 
    /// - this will probably switch to a new page to show a detail view.
    /// </summary>
    /// <param name="ms">The MealSummary to show</param>
    /// <returns></returns>
    [RelayCommand]
    private async Task InvokeShowDetails(MealSummary ms)
    {
        ms = BestMealSummary(ms);
        if (ms is not null)
            await ShowDetailsParam?.Invoke(ms);
    }

    /// <summary>
    /// Select the next sort order. Cycles through the available sort orders one at a time then restarts at the first one again.
    /// </summary>
    [RelayCommand]
    private void ChangeSort() => NextSortOrder();

    /// <summary>
    /// Turn on or off the single person filter (to show only items they share)
    /// </summary>
    [RelayCommand]
    private void ChangeFilter() => Filter = !Filter;

    /// <summary>
    /// Show or hide the local meals. 
    /// </summary>
    [RelayCommand]
    private async Task ChangeShowLocalMeals() 
    {
        try
        {
            IsMealListLoading = true;
            if (!ShowLocalMeals)
                await Meal.GetLocalMealListAsync();
            ShowLocalMeals = !ShowLocalMeals;
        }
        catch (Exception)
        { // If anything went wrong make sure the client knows
            ShowLocalMeals = false; // Make it clear there is something wrong with the meal list
        }
        finally
        {
            IsMealListLoading = false; // Indicate that we're done attempting to load 
        }
    }

    /// <summary>
    /// Show or hide remote meals (meals held by the web service)
    /// </summary>
    [RelayCommand]
    private async Task ChangeShowRemoteMeals()
        {
            try
            {
                if (App.Settings.IsCloudAccessAllowed)
                {
                    IsMealListLoading = true;
                    if (ShowRemoteMeals)
                        ShowRemoteMeals = false;
                    else
                    {
                        if (await Meal.GetRemoteMealListAsync())
                            ShowRemoteMeals = true;
                        else
                        {
                            IsMealListLoading = false;
                            await ShowRemoteAccessWarning();
                        }
                    }
                }
                else if (!ShowRemoteMeals)
                {
                    if (App.IsLimited)
                        await Utilities.DisplayAlertAsync("Cloud Archive Unavailable", "Cloud archiving is not supported in Basic Edition");
                    else
                    {
                        App.Settings.IsCloudAccessAllowed = await Utilities.AskAsync("Cloud Archive is Off", "The 'Allow Archive to " +
                            "Cloud' program setting is off. Do you want to turn it on?", "Yes", "No");
                        if (App.Settings.IsCloudAccessAllowed)
                            await ChangeShowRemoteMeals();
                    }
                }
                else
                    await ShowRemoteAccessWarning();
            }
            catch (Exception)
            { // If anything went wrong make sure the client knows
                ShowRemoteMeals = false; // Make it clear there is something wrong with the remote meal list
            }
            finally
            {
                IsMealListLoading = false; 
            }
        }

    /// <summary>
    /// Notify the user that they attempted something that requires remote access and it's not available
    /// </summary>
    private async Task ShowRemoteAccessWarning()
    {
        await Utilities.ShowAppSnackBarAsync("Warning: Remote Access is not currently available");
    }

    /// <summary>
    /// Make the Meal corresponding to this MealSummary the current one (which may 
    /// save the previous one if it isn't saved already).
    /// </summary>
    /// <param name="ms">The MealSummary for the Meal which is to be made current</param>
    [RelayCommand]
    private async Task InvokeUseMeal(MealSummary ms) => await UseMealParam?.Invoke(BestMealSummary(ms));

    #region Delete / Undelete
    [RelayCommand]
    private async Task DeleteLocalMeals() => await DeleteAnyMeal(true, false);

    [RelayCommand]
    private async Task DeleteRemoteMeals() => await DeleteAnyMeal(false, true);

    [RelayCommand]
    private async Task DeleteMeal(MealSummary ms)
    {
        if (ms is not null)
        {
            if (ms == SelectedMealSummary)
                SelectedMealSummary = MealList.Alternate(ms);
            await DeleteOneMeal(ms, true, true);
        }
        else
            await DeleteAnyMeal(true, true);
    }
    private async Task DeleteAnyMeal(bool tryLocal, bool tryRemote)
    {
        if (IsSelectableList)
        {
            List<MealSummary> list = new();
            int failed = 0;
            try
            {
                list = new(MealList.Where(ms => ms.FileSelected && ((tryLocal && ms.IsLocal) || (tryRemote && ms.IsRemote) && !ms.IsBusy))); // need a separate list so as not to disturb the iterator
                Task<int> task = DeleteMultipleMeals(list, tryLocal, tryRemote);
                Task whichTask = await Task.WhenAny(Task.Delay(500), task);
                // Deletes, especially local ones, are really fast, so don't bother to show a busy indication unless they take a while
                if (whichTask != task)
                {
                    IsBusy = true;
                    foreach (var ms in list) ms.IsBusy = true;
                }
                failed = await task;
            }
            finally
            {
                if (IsBusy || failed != 0) // The delete took a while or was only partially successful
                {
                    if (SelectedMealSummary is not null && !SelectedMealSummary.IsLocal && !SelectedMealSummary.IsRemote)
                        SelectedMealSummary = null;
                    int succeeded = list.Count - failed;
                    await Task.Delay(1000);
                    IsBusy = false;
                    foreach (var ms in list) ms.IsBusy = false;
                    if (failed == 0)
                        await Utilities.ShowAppSnackBarAsync($"{succeeded} bills deleted");
                    else
                        await Utilities.ShowAppSnackBarAsync($"{succeeded} of {list.Count} bills deleted");
                }
            }
        }
        else if (SelectedMealSummary is not null)
        {
            var mealToDelete = SelectedMealSummary; // Because deleting it
            var next = MealList.Alternate(mealToDelete);
            await DeleteOneMeal(mealToDelete, tryLocal, tryRemote);
            // If the meal is not showing any more select the next one
            if (!(mealToDelete.IsLocal && ShowLocalMeals) 
                && !(mealToDelete.IsRemote && ShowRemoteMeals))
                SelectedMealSummary = next;
        }
    }

    /// <summary>
    /// Delete any meals in the passed list and mark them as not busy
    /// </summary>
    /// <param name="list">The list of meals to delete</param>
    /// <param name="tryLocal">Delete local files from list</param>
    /// <param name="tryRemote">Delete remote file from list</param>
    /// <returns>How many were not deleted</returns>
    private async Task<int> DeleteMultipleMeals(List<MealSummary> list, bool tryLocal, bool tryRemote)
    {
        ProgressLimit = list.Count;
        Progress = 0;
        int attempted = 0;
        cancellationTokenSource = new CancellationTokenSource();
        foreach (MealSummary mealSummary in list)
        {
            await Task.Delay(1000);
            if (cancellationTokenSource.IsCancellationRequested)
                break;
            await DeleteOneMeal(mealSummary, tryLocal, tryRemote);
            mealSummary.IsBusy = false;
            attempted++;
            Progress = (double)attempted / ProgressLimit;
        }
        return ProgressLimit - attempted; 
    }

    /// <summary>
    /// Delete the Meal represented by this MealSummary. Only delete it from one place at a time, 
    /// so if it is both local and remote only delete the local one.
    /// </summary>
    /// <param name="ms">The target MealSummary</param>
    /// <returns></returns>
    private async Task DeleteOneMeal(MealSummary ms, bool tryLocal, bool tryRemote)
    {
        if (ms.IsForCurrentMeal && ms.IsLocal && tryLocal)
        {
            if (IsSelectableList)
                await Utilities.DisplayAlertAsync("Error", $"\"{ms.VenueName} - {ms.CreationTime:g} {ms.ApproximateAge}\" is the current bill, you must select another before deleting it");
            else
                await Utilities.DisplayAlertAsync("Error", "This is the current bill, you must select another before deleting it");
        }
        else
        {
            bool doLocal = tryLocal && ms.IsLocal; // Remove local version
            bool doRemote = tryRemote && ms.IsRemote; // Remove remote version
            // only ever delete the meal from one place at a time
            await ms.DeleteAsync(doLocal: doLocal, doRemote: doRemote && !doLocal); // If it is both local and remote remove the local one only
            NoteDeletedChange();
        }
    }

    /// <summary>
    /// Called whenever the number of deleted MealSummary objects might have changed, causes ...Deleted properties to be reevaluated
    /// </summary>
    private void NoteDeletedChange()
    {
        OnPropertyChanged(nameof(AnyDeleted));
        OnPropertyChanged(nameof(ManyDeleted));
    }
    public void CheckDeleted() => NoteDeletedChange();
    
    /// <summary>
    /// Discard any list of deleted MealSummary objects (usually called when closing a MealListPage
    /// </summary>
    public void ForgetDeleted()
    {
        MealSummary.ForgetDeleted();
        NoteDeletedChange();
    }
    public bool AnyDeleted => MealSummary.DeletedStack.Any();
    public bool ManyDeleted => MealSummary.DeletedStack.Skip(1).Any();

    /// <summary>
    /// Restore a previously deleted MealSummary - note that it will be restored to the same place in the list that it was before it was removed
    /// TODO: Deal with the rare case where an undeleted file should not be visible (it is local only and we're only showing remote files)
    /// This is rare because local only files are automatically backed up to the cloud
    /// </summary>
    [RelayCommand]
    private void Undelete()
    {
        MealSummary ms = MealSummary.PopMostRecentDeletion();
        if (ms is not null)
        {
            ms.UnDelete();
            NoteDeletedChange();
        }
    }

    /// <summary>
    /// Restore all deleted MealSummary objects, done by restoring the most recently deleted one, then the next most recently deleted one and so on
    /// </summary>
    [RelayCommand]
    private void UndeleteAll()
    {
        while (AnyDeleted)
            Undelete();
    }
    #endregion

    /// <summary>
    /// Check that the Meal corresponding to a particular MealSummary object is downloadable (is it remote, is it not local and so on)
    /// </summary>
    /// <param name="ms">The target MealSummary</param>
    /// <returns></returns>
    private bool CanDownLoadMeal(MealSummary ms)
        => ms is not null
        && App.Settings.IsCloudAccessAllowed
        && ms.IsRemote
        && !ms.IsLocal;

    /// <summary>
    /// Command to download one or more meals, note that the corresponding command is always enabled but gives haptic feedback if 
    /// you try and download in error  
    /// </summary>
    /// <param name="ms">If a single meal is identified for download, this is it</param>
    /// <returns></returns>
    [RelayCommand]
    private async Task Download(MealSummary ms)
    {
        int failed = 0;
        int succeeded = 0;

        if (ms is not null)
        {
            failed = await DownloadOneMeal(ms) ? 0 : 1;
            succeeded = 1 - failed;
        }
        else if (IsSelectableList)
        {
            try
            {
                Task<(int, int)> task = DownloadMultipleMeals();
                Task whichTask = await Task.WhenAny(Task.Delay(500), task);
                if (whichTask != task)
                    IsBusy = true;
                (succeeded, failed) = await task;
            }
            finally
            {
                if (IsBusy)
                {
                    await Task.Delay(1000);
                    IsBusy = false;
                }
            }
        }
        else if (SelectedMealSummary is not null)
        {
            failed = await DownloadOneMeal(SelectedMealSummary) ? 0 : 1;
            succeeded = 1 - failed;
        }
        if (failed == 0)
        {
            if (succeeded != 1)
                await Utilities.ShowAppSnackBarAsync($"Downloaded {succeeded} bills");
            else
                await Utilities.ShowAppSnackBarAsync("One Bill Downloaded"); // there was only one so no need for a count information
        }
        else
        {
            await Utilities.HapticNotify();
            if (IsSelectableList)
                await Utilities.ShowAppSnackBarAsync($"Downloaded {succeeded} bills, {failed} failed");
            else
                await Utilities.ShowAppSnackBarAsync("Download failed"); // there was only one so no need for a count information
        }
    }


    /// <summary>
    /// Download selected files, note that you can select files which are already downloaded, in which case the download will fail
    /// </summary>
    /// <returns>The number of failed downloads</returns>
    private async Task<(int Succeeded, int Failed)> DownloadMultipleMeals()
    {
        int failed = 0;
        var list = new List<MealSummary>(MealList.Where(ms => ms.FileSelected && !ms.IsLocal && !ms.IsBusy)); // a separate list so as to ignore updates
        ProgressLimit = list.Count;
        Progress = 0;
        int attempted = 0;
        cancellationTokenSource = new CancellationTokenSource();
        AwaitableQueue<MealSummary> downloadedQueue = new();
        Task locationChanger = new Task(async () =>
            {
                while (true)
                {
                    var ms = await downloadedQueue.DequeueAsync(CancellationToken.None);
                    if (ms is null) break;
                    ms.LocationChanged(isLocal: true);
                    ms.IsBusy = false;
                }
            }, CancellationToken.None);

        locationChanger.Start();
        try
        {
            ParallelOptions parallelOptions = new()
            {
                MaxDegreeOfParallelism = -1, // Whatever the system can handle
                CancellationToken = cancellationTokenSource.Token
            };
            foreach (var ms in list) ms.IsBusy = true;
            Task downLoad = Parallel.ForEachAsync(list, parallelOptions, async (mealSummary, cancellationToken) =>
            {
                if (cancellationToken.IsCancellationRequested) throw new TaskCanceledException(); 
                bool worked = await DownloadOneMeal(mealSummary, false, cancellationToken);
                // In order not to multi-thread access to LocalMealList we just queue the changed mealsummaries and handle them all on one thread
                if (worked)
                    downloadedQueue.Enqueue(mealSummary);
                else
                    Interlocked.Increment(ref failed);
                Interlocked.Increment(ref attempted);
                Progress = (double)attempted / ProgressLimit;
            });

            await downLoad;
            downloadedQueue.Enqueue(null);
        }
        catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException)
        {
            failed += ProgressLimit - attempted; // then just continue, no need to report the error
            if (!locationChanger.IsCompleted)
            {
                downloadedQueue.Enqueue(null);
                await locationChanger;
            }
            foreach (var ms in list) ms.IsBusy = false;
        }
        return (attempted - failed, failed);
    }
    /// <summary>
    /// Download a single meal from the cloud to local storage  
    /// </summary>
    /// <param name="ms"></param>
    /// <returns>true if the meal was downloaded false otherwise</returns>
    private async Task<bool> DownloadOneMeal(MealSummary ms, bool changeLocation = true, CancellationToken ct = default)
    {
        try
        {
            if (CanDownLoadMeal(ms))
            {
                Meal m = await Meal.LoadFromRemoteAsync(ms);
                if (m is not null)
                {
                    await m.SaveToFileAsync();
                    if (changeLocation)
                        ms.LocationChanged(isLocal: true);
                    return true;
                }
            }
        }
        finally
        {
            ms.IsBusy = false;
        }
        return false;
    }

    private CancellationTokenSource cancellationTokenSource = null;

    [RelayCommand]
    private void Cancel() => cancellationTokenSource?.Cancel();

    [RelayCommand]
    private void SelectMeal(MealSummary ms)
    {
        if (ms is null) return;
        if (IsSelectableList)
        {
            ms.FileSelected = !ms.FileSelected;
            SelectedMealSummariesCount += (ms.FileSelected) ? 1 : -1;
        }
        else
            SelectedMealSummary = SelectedMealSummary == ms ? null : ms;
    }

    [RelayCommand]
    private void SelectNone()
    {
        IsSelectableList = true;
        foreach (var mealSummary in MealList.Where(ms=>ms.FileSelected))
            mealSummary.FileSelected = false;
        SelectedMealSummariesCount = 0;
    }

    /// <summary>
    /// Select all but the current meal.
    /// Leave the current meal selection state unchanged.
    /// </summary>
    [RelayCommand]
    private void SelectAll()
    {
        IsSelectableList = true;

        int howMany = 0;
        foreach (var ms in MealList)
        {
            if (ms.IsForCurrentMeal)
                howMany += ms.FileSelected ? 1 : 0;
            else
            {
                howMany++;
                ms.FileSelected = true; 
            }
        }
        SelectedMealSummariesCount = howMany;
    }

    [RelayCommand]
    private void InvertSelection()
    {
        IsSelectableList = true;
        int howMany = 0; // An optimization to save repeated updates of SelectedMealSummariesCount
        foreach (var ms in MealList)
        {
            ms.FileSelected = !ms.FileSelected;
            if (ms.FileSelected)
                howMany++;
        }
        SelectedMealSummariesCount = howMany;
    }

    /// <summary>
    /// Force future callers to reevaluate the list because we know it has changed
    /// </summary>
    public void InvalidateMealList()
    {
        MealList = null; // The list is not accurate any more
    }

    public void DeselectInvisibleMeals() 
    {
        int howMany = 0;
        foreach (var mealSummary in MealList.Where(ms => ms.FileSelected && !(ShowRemoteMeals || ms.IsLocal && ShowLocalMeals)))
        {
            mealSummary.FileSelected = false;
            howMany++;
        }
        SelectedMealSummariesCount -= howMany;
    }

    public string SortOrderName => SortOrder == SortOrderType.byName ? "name" : SortOrder == SortOrderType.byDate ? "age" : SortOrder == SortOrderType.byDistance ? "distance" : "unknown";
    public enum SortOrderType { byDate, byDistance, byName };

    private SortOrderType sortOrder = SortOrderType.byDate;
    public void NextSortOrder()
    {
        if (SortOrder == Enum.GetValues(typeof(SortOrderType)).Cast<SortOrderType>().Max())
            SortOrder = Enum.GetValues(typeof(SortOrderType)).Cast<SortOrderType>().Min();
        else
            SortOrder++;
    }
    public SortOrderType SortOrder
    {
        get => sortOrder;
        set => SetProperty(ref sortOrder, value, () => { InvalidateMealList(); OnPropertyChanged(nameof(SortOrderName)); });
    }
    private string Sort
    {
        get => Enum.GetName(typeof(SortOrderType), sortOrder);
        set
        {
            string sortRequest = Uri.UnescapeDataString(value ?? string.Empty);
            SortOrder = sortRequest.Equals("name") ? SortOrderType.byName : SortOrderType.byDate;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterGlyph))]
    [NotifyPropertyChangedFor(nameof(FilterText))]
    private bool filter = false;

    partial void OnFilterChanged(bool value) => InvalidateMealList(); // DeselectInvisibleMeals() is not needed here because the filtering code handles it
    public FontImageSource FilterGlyph => (FontImageSource)(Filter ? Application.Current.Resources["GlyphFilterOn"] : Application.Current.Resources["GlyphFilterOff"]);
    #region Show/Hide Local/Remote
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLocalText))]
    [NotifyPropertyChangedFor(nameof(WhereText))]
    private bool showLocalMeals = true;
    partial void OnShowLocalMealsChanged(bool value) { if (!value) DeselectInvisibleMeals(); InvalidateMealList(); }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRemoteText))]
    [NotifyPropertyChangedFor(nameof(WhereText))]
    private bool showRemoteMeals = false;
    partial void OnShowRemoteMealsChanged(bool value) { if (!value) DeselectInvisibleMeals(); InvalidateMealList(); }
    public string ShowRemoteText => ShowRemoteMeals ? "Hide Remote" : "Show Remote";
    public string ShowLocalText => ShowLocalMeals ? "Hide Local" : "Show Local";
    public string WhereText => ShowLocalMeals == ShowRemoteMeals ? null : ShowLocalMeals ? "local" : "remote";
    public string FilterText => Filter ? "Show Bills" : "Show Venues";
    #region Meal local/remote status icons
    public bool Dark => Application.Current.UserAppTheme == AppTheme.Dark || Application.Current.RequestedTheme == AppTheme.Dark;
    #endregion 
    #endregion

    [ObservableProperty]
    private bool isCloudAllowed = App.IsCloudAllowed;

    [ObservableProperty]
    private double progress = 0;

    [ObservableProperty]
    private int progressLimit = 0;

    [ObservableProperty]
    private bool isSelectableList = false;
    partial void OnIsSelectableListChanged(bool value)
    {
        if (IsSelectableList)
            MealCollectionMode = SelectionMode.None; // This is not a typo, we manage SelectedMealSummaries ourselves
        else
            MealCollectionMode = SelectionMode.Single;
        SetSelectedMealSummariesCountText();
    }

    [ObservableProperty]
    private MealSummary selectedMealSummary;

    int selectedMealSummariesCount = 0;
    public int SelectedMealSummariesCount
    {
        get => selectedMealSummariesCount;
        private set
        {
            if (selectedMealSummariesCount != value)
            {
                selectedMealSummariesCount = value;
                SetSelectedMealSummariesCountText();
            }
        }
    }
    public bool SetCount { get => false; set => SetSelectedMealSummariesCount(); }
    private void SetSelectedMealSummariesCount() => SelectedMealSummariesCount = MealList.Count(ms => ms.FileSelected);
    private void SetSelectedMealSummariesCountText() => SelectedMealSummariesCountText = SelectedMealSummariesCount > 0 & IsSelectableList ? SelectedMealSummariesCount.ToString() : null;

    [ObservableProperty]
    string selectedMealSummariesCountText = null;

    [ObservableProperty]
    SelectionMode mealCollectionMode = SelectionMode.Single;

    [ObservableProperty]
    private bool isMealListLoading;

    private ObservableCollection<MealSummary> mealList = null;
    /// <summary>
    /// Return a list of MealSummary items created by selecting either the local or remote list or merging 
    /// the local and remote lists together. It's also possible to filter the list contents to show only the latest 
    /// MealSummary for each venue.  When merging is being done we have to eliminate duplicate instances of the same MealSummary 
    /// object from the two lists.
    /// </summary>
    public ObservableCollection<MealSummary> MealList
    {
        get
        {
            // Local function

            ObservableCollection<MealSummary> GetList()
            {
                if (ShowLocalMeals)
                {
                    if (ShowRemoteMeals)
                        return new ObservableCollection<MealSummary>(Meal.LocalMealList.Union(Meal.RemoteMealList).OrderByDescending(ms => ms.CreationTime)); // merge the two lists
                    else
                        return new ObservableCollection<MealSummary>(Meal.LocalMealList);
                }
                else if (ShowRemoteMeals)
                    return new ObservableCollection<MealSummary>(Meal.RemoteMealList);
                else // should never happen
                    return new ObservableCollection<MealSummary>();
            }
            
            static IOrderedEnumerable<MealSummary> SortByDistance(IEnumerable<MealSummary> mealSummaries)
            {
                if (App.MyLocation is null)
                    return mealSummaries.OrderBy((ms) => ms.VenueName);
                return mealSummaries.OrderBy((ms) => ms.Distance).ThenBy((ms) => ms.VenueName).ThenByDescending((ms) => ms.CreationTime);
            }

            // Begin MealList 'get' code

            if (mealList is not null)
                return mealList; // There's a cached one, just use it
            ObservableCollection<MealSummary> theList = GetList();
            if (theList.Count <= 1)
                return mealList = theList; // If there are one or fewer meals, sort order and filtering are irrelevant
            else if (Filter)
            {
                // We could perhaps optimize this by sorting theList in place then deleting the duplicates,
                // but the performance gain doesn't seem worth the trouble.
                List<MealSummary> filteredList = theList.OrderBy(ms => ms.VenueName).ThenByDescending((ms) => ms.CreationTime).ToList(); 
                for (int i = filteredList.Count-1; i > 0; i--)
                {
                    MealSummary ms = filteredList[i];
                    if (ms.VenueName.Equals(filteredList[i-1].VenueName))
                    {
                        // This is a later bill for the same venue, discard it
                        if (ms.FileSelected) // Make sure invisible meals are not selected
                            ms.FileSelected = false; // we'll fix up the summaries count later
                        filteredList.RemoveAt(i);
                    }
                }
                theList = new(filteredList);
                // At this point all the duplicate dates for the same venue are gone and we just have a list in venue name order
                // now fill in the distances in case we need to sort on them.
                if (App.MyLocation is not null)
                    foreach (MealSummary ms in theList)
                    {
                        Venue v = Venue.FindVenueByName(ms.VenueName);
                        ms.Distance = v is null ? Distances.Unknown : v.SimplifiedDistance;
                    }
                // Now we have the correct list, sort it
                if (SortOrder == SortOrderType.byName)
                    mealList = theList;
                else if (SortOrder == SortOrderType.byDistance)
                    mealList = new ObservableCollection<MealSummary>(SortByDistance(theList));
                else //  SortOrder == SortOrderType.byDate (creation time order)
                    mealList = new ObservableCollection<MealSummary>(theList.OrderByDescending((ms) => ms.CreationTime));
            }
            else // Not filtered
            {
                if (App.MyLocation is not null)
                    foreach (MealSummary ms in theList)
                    {
                        Venue v = Venue.FindVenueByName(ms.VenueName);
                        ms.Distance = v is null ? Distances.Unknown : v.SimplifiedDistance;
                    }
                if (SortOrder == SortOrderType.byName)
                    mealList = new ObservableCollection<MealSummary>(theList.OrderBy((ms) => ms.VenueName));
                else if (SortOrder == SortOrderType.byDistance)
                    mealList = new ObservableCollection<MealSummary>(SortByDistance(theList));
                else // SortOrder == SortOrderType.byDate (creation time order)
                    mealList = theList;
            }
            SelectedMealSummariesCount = mealList.Count(ms => ms.FileSelected);
            return mealList;
        }
        private set => SetProperty(ref mealList, value);
    }
    private bool UpsertIntoMealList(MealSummary ms)
    {
        switch (SortOrder)  
        {
            case SortOrderType.byDistance:
                return mealList.Upsert(ms, MealSummary.CompareDistanceTo);
            case SortOrderType.byName:
                return mealList.Upsert(ms, MealSummary.CompareVenueTo);
            case SortOrderType.byDate:
            default:
                return mealList.Upsert(ms, MealSummary.CompareCreationTimeTo);
        }
    }
}
