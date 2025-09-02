using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Models;
using DivisiBill.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace DivisiBill.ViewModels;

/// <summary>
/// This manages the list of bills sorted and filtered in various ways. The contents of the list (MealList) is composed of the same set of MealSummaries that other instances see so setting some
/// property on one propagates to other views.
/// </summary>
public partial class MealListViewModel : ObservableObjectPlus, IQueryAttributable
{
    public Func<MealSummary, Task> UseMealParam { get; set; }
    public Func<MealSummary, Task> ShowDetailsParam { get; set; }

    public MealListViewModel()
    {
        Meal.LocalMealList.CollectionChanged += LocalMealList_CollectionChanged;
        Meal.RemoteMealList.CollectionChanged += RemoteMealList_CollectionChanged;
        App.MyLocationChanged += App_MyLocationChanged;
        Venue.DistanceChanged += Venue_DistanceChanged;
        scrollEndTimer = new(_ => MainThread.InvokeOnMainThreadAsync(() => IsMealListScrollingFar = false), null, Timeout.Infinite, Timeout.Infinite);
    }
    private void Venue_DistanceChanged(object sender, VenueDistanceChangedEventArgs e)
    {
        var changedMeals = Meal.LocalMealList.Where(m => m.VenueName.Equals(e.Venue.Name));
        foreach (MealSummary ms in changedMeals)
            ms.Distance = e.Venue.Distance; // Update the distance for the changed venue
        if (SortOrder == SortOrderType.byDistance)
        {
            if (IsGrouped)
            {
                MealSummaryGroup groupForChangedVenue = MealSummaryGroups.FirstOrDefault(g => g.VenueName.Equals(e.Venue.Name));
                if (groupForChangedVenue is null) // If the group for the changed venue is not in the list then something is wrong
                    InvalidateMealList(); // Force the list to be rebuilt
                else
                {
                    groupForChangedVenue.NotifyDistanceChanged(); // Update the distance for the group
                    UpsertIntoMealSummaryGroupList(groupForChangedVenue); // Update the group
                }
            }
            else
            {
                foreach (MealSummary ms in changedMeals)
                    UpsertIntoMealList(ms);
            }
        }
        else if (IsGrouped)
        {
            MealSummaryGroup groupForChangedVenue = MealSummaryGroups.FirstOrDefault(g => g.VenueName.Equals(e.Venue.Name));
            if (groupForChangedVenue is null) // If the group for the changed venue is not in the list then something is wrong
                InvalidateMealList(); // Force the list to be rebuilt
            else
                groupForChangedVenue.NotifyDistanceChanged(); // Update the distance for the group
        }
    }
    private async void App_MyLocationChanged(object sender, EventArgs e)
    {
        await Venue.UpdateAllDistances();
        if (SortOrder == SortOrderType.byDistance)
            InvalidateMealList();
    }
    ~MealListViewModel()
    {
        Meal.LocalMealList.CollectionChanged -= LocalMealList_CollectionChanged;
        Meal.RemoteMealList.CollectionChanged -= RemoteMealList_CollectionChanged;
        App.MyLocationChanged -= App_MyLocationChanged;
        Venue.DistanceChanged -= Venue_DistanceChanged;
        scrollEndTimer.Dispose();
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

    private Meal lastCurrentShown = null;
    public void OnNavigatedTo()
    {
        if (Meal.CurrentMeal != lastCurrentShown)
        {
            ShowCurrent();
            lastCurrentShown = Meal.CurrentMeal;
        }
    }

    /// <summary>
    /// Called whenever the local Meal list changes, which can happen if asynchronous restore or recover operations are in process
    /// or if asynchronous file cleanup is in process. Its basic job is to keep the displayed list in sync with the changes if necessary.
    /// </summary>
    private void LocalMealList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (mealList is null || !ShowLocalMeals)
            return;
        if (e.Action == NotifyCollectionChangedAction.Remove)
            foreach (MealSummary ms in e.OldItems)
            {
                if (!(ms.IsRemote && ShowRemoteMeals)) // in other words if not showing because of the other list
                    if (mealList.Remove(ms)) // DeselectInvisibleMeals() is not needed here because we handle it directly
                    {
                        if (IsGrouped)
                        {   // Remove from the group
                            MealSummaryGroup group = MealSummaryGroups.FirstOrDefault(g => g.VenueName == ms.VenueName);
                            group.MealSummaries.Remove(ms);
                            if (group.Count == 0)
                                MealSummaryGroups.Remove(group);
                            else if (group.CreationTime == ms.CreationTime)
                            {
                                group.CreationTime = group.MealSummaries[0].CreationTime;
                                if (UpsertIntoMealSummaryGroupList(group))
                                {
                                    int index = MealSummaryGroups.IndexOf(group);
                                    ScrollItemsTo(index, ScrollToPosition.Start);
                                }
                            }
                        }
                        if (ms.FileSelected)
                        {
                            ms.FileSelected = false;
                            SelectedMealSummariesCount--;
                        }
                    }
            }
        else if (e.Action == NotifyCollectionChangedAction.Add) // Probably an Undelete operation
            foreach (MealSummary ms in e.NewItems)
            {
                if (!(ms.IsRemote && ShowRemoteMeals) // in other words if not in the other list
                    && UpsertIntoMealList(ms)) // DeselectInvisibleMeals() is not needed here because we handle it directly
                {
                    if (IsGrouped)
                    {   // Add to the group
                        MealSummaryGroup group = MealSummaryGroups.FirstOrDefault(g => g.VenueName == ms.VenueName);
                        if (group is not null)
                        {
                            group.MealSummaries.Upsert(ms, MealSummary.CompareCreationTimeTo);
                            if (group.CreationTime < ms.CreationTime)
                            {
                                group.CreationTime = ms.CreationTime;
                                if (UpsertIntoMealSummaryGroupList(group))
                                {
                                    int index = MealSummaryGroups.IndexOf(group);
                                    ScrollItemsTo(index, ScrollToPosition.Start);
                                }
                            }
                        }
                        else
                        {
                            group = new MealSummaryGroup(ms);
                            UpsertIntoMealSummaryGroupList(group);
                        }
                    }
                    if (ms.FileSelected)
                    {
                        SelectedMealSummariesCount++;
                    }
                }
            }
        else
        if (e.Action == NotifyCollectionChangedAction.Reset)
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
        if (mealList is null || !ShowRemoteMeals)
            return;
        if (e.Action == NotifyCollectionChangedAction.Remove)
            foreach (MealSummary ms in e.OldItems)
            {
                if (!(ms.IsLocal && ShowLocalMeals)) // in other words if not already in the list
                    if (mealList.Remove(ms)) // DeselectInvisibleMeals() is not needed here because we handle it directly
                    {
                        if (IsGrouped)
                        {   // Remove from the group
                            MealSummaryGroup group = MealSummaryGroups.FirstOrDefault(g => g.VenueName == ms.VenueName);
                            group.MealSummaries.Remove(ms);
                            if (group.Count == 0)
                                MealSummaryGroups.Remove(group);
                            else if (group.CreationTime == ms.CreationTime)
                            {
                                group.CreationTime = group.MealSummaries[0].CreationTime;
                                UpsertIntoMealSummaryGroupList(group);
                            }
                        }
                        if (ms.FileSelected)
                        {
                            ms.FileSelected = false;
                            SelectedMealSummariesCount--;
                        }
                    }
            }
        else if (e.Action == NotifyCollectionChangedAction.Add) // Probably an Undelete operation
            foreach (MealSummary ms in e.NewItems)
            {
                if (!(ms.IsLocal && ShowLocalMeals)  // in other words if not in the other list
                    && UpsertIntoMealList(ms)) // DeselectInvisibleMeals() is not needed here because we handle it directly
                {
                    if (IsGrouped)
                    {   // Add to the group
                        MealSummaryGroup group = MealSummaryGroups.FirstOrDefault(g => g.VenueName == ms.VenueName);
                        if (group is not null)
                        {
                            if (group.CreationTime < ms.CreationTime)
                                group.CreationTime = ms.CreationTime;
                            group.MealSummaries.Upsert(ms, MealSummary.CompareCreationTimeTo);
                        }
                        else
                        {
                            group = new MealSummaryGroup(ms);
                            UpsertIntoMealSummaryGroupList(group);
                        }
                    }
                    if (ms.FileSelected)
                    {
                        SelectedMealSummariesCount++;
                    }
                }
            }
        else
        if (e.Action == NotifyCollectionChangedAction.Reset)
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
    /// Determines the best meal summary to use based on the provided input and the current state of the meal selection.
    /// </summary>
    /// <remarks>This method prioritizes the provided meal summary. If the input is null, it
    /// falls back to the first selected meal in the list, provided the list is selectable. If no meals are selected or
    /// the list is not selectable, the currently selected meal summary is used as a final fallback.</remarks>
    /// <param name="ms">The meal summary to evaluate. If not null, this value will be returned.</param>
    /// <returns>The provided <paramref name="ms"/> if it is not null; otherwise, the first meal summary from the meal list where
    /// the file is selected, if the list is selectable. If no such meal exists or the list is not selectable, the
    /// currently selected meal summary is returned.</returns>
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
    private void ChangeFilter() => IsGrouped = !IsGrouped;

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
            IsCloudAllowed = App.IsCloudAllowed; // Just in case it changed
            if (ShowRemoteMeals)
                ShowRemoteMeals = false;
            else if (App.IsCloudAllowed)
            {
                IsMealListLoading = true;
                if (await Meal.GetRemoteMealListAsync())
                    ShowRemoteMeals = true;
                else
                {
                    IsMealListLoading = false;
                    await Utilities.ShowAppSnackBarAsync("Warning: Remote bill list Failed to load");
                }
            }
            else if (!ShowRemoteMeals)
            {
                if (await App.Current.RequestCloudBackup())
                    await ChangeShowRemoteMeals();
            }
            else
                await Utilities.ShowAppSnackBarAsync("Warning: Remote Access is not currently available");
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
            List<MealSummary> list = [];
            int failed = 0;
            try
            {
                list = [.. MealList.Where(ms => ms.FileSelected && ((tryLocal && (ms.IsLocal || ms.IsFake)) || (tryRemote && ms.IsRemote) && !ms.IsBusy))]; // need a separate list so as not to disturb the iterator
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
                    if (succeeded == 0)
                        await Utilities.ShowAppSnackBarAsync("No bills deleted"); // there was only one so no need for a count information
                    else if (failed == 0)
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
        else if (IsGrouped && SelectedGroup is not null)
        {
            foreach (MealSummary mealToDelete in SelectedGroup.MealSummaries.Reverse())
            {
                await DeleteOneMeal(mealToDelete, tryLocal, tryRemote);
            }
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
        int attempted = 0, succeeded = 0;
        cancellationTokenSource = new CancellationTokenSource();
        foreach (MealSummary mealSummary in list)
        {
            if (cancellationTokenSource.IsCancellationRequested)
                break;

            if (await DeleteOneMeal(mealSummary, tryLocal, tryRemote))
                succeeded++;
            mealSummary.IsBusy = false;
            attempted++;
            Progress = (double)attempted / ProgressLimit;
        }
        return ProgressLimit - succeeded;
    }

    /// <summary>
    /// Delete the Meal represented by this MealSummary. Only delete it from one place at a time, 
    /// so if it is both local and remote only delete the local one.
    /// </summary>
    /// <param name="ms">The target MealSummary</param>
    /// <returns>False if the bill was the current one (and so not deleted), false if a bill was deleted</returns>
    private async Task<bool> DeleteOneMeal(MealSummary ms, bool tryLocal, bool tryRemote)
    {
        if (ms.IsForCurrentMeal && ms.IsLocal && tryLocal)
        {
            if (IsSelectableList)
                await Utilities.DisplayAlertAsync("Error", $"\"{ms.VenueName} - {ms.CreationTime.ApproximateDateTime()}\" is the current bill, you must select another before deleting it");
            else
                await Utilities.DisplayAlertAsync("Error", "This is the current bill, you must select another before deleting it");
            return false;
        }
        else
        {
            bool doLocal = tryLocal && ms.IsLocal; // Remove local version
            bool doRemote = tryRemote && ms.IsRemote; // Remove remote version
            // only ever delete the meal from one place at a time
            await ms.DeleteAsync(doLocal: doLocal, doRemote: doRemote && !doLocal); // If it is both local and remote remove the local one only
            NoteDeletedChange();
            return true;
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
    public bool AnyDeleted => MealSummary.DeletedStack.Count > 0;

    public bool ManyDeleted => MealSummary.DeletedStack.Count > 1;

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
    private static bool CanDownLoadMeal(MealSummary ms)
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
        Task locationChanger = new(async () =>
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
                bool worked = await DownloadOneMeal(mealSummary, false);
                // In order not to multi-thread access to LocalMealList we just queue the changed meal summaries and handle them all on one thread
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
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
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
    private static async Task<bool> DownloadOneMeal(MealSummary ms, bool changeLocation = true)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(ms);
            if (!ms.IsRemote || !App.Settings.IsCloudAccessAllowed)
                return false;
            else
            {
                bool didSomething = false;
                if (!ms.IsLocal)
                {
                    Meal m = await Meal.LoadFromRemoteAsync(ms);
                    if (m is not null)
                    {
                        await m.SaveToFileAsync();
                        if (changeLocation)
                            ms.LocationChanged(isLocal: true);
                    }
                    didSomething = true;
                }
                if (App.IsCloudImageBackupAllowed && ms.HasRemoteImage && !ms.HasImage) // Meaning there is a remote image but not a local one
                {
                    // Load the remote image as well
                    if (await Meal.LoadImageFromRemoteAsync(ms))
                        didSomething = true;
                }
                return didSomething;
            }
        }
        finally
        {
            ms.IsBusy = false;
        }
    }

    private CancellationTokenSource cancellationTokenSource = null;
    #region Commands
    [RelayCommand]
    private void Cancel() => cancellationTokenSource?.Cancel();

    [RelayCommand]
    private void ShowCurrent()
    {
        int targetIndex = -1;
        if (IsGrouped)
        {
            var targetGroup = MealSummaryGroups.FirstOrDefault(g => g.VenueName == Meal.CurrentMeal.VenueName);
            if (targetGroup is not null)
                targetIndex = MealSummaryGroups.IndexOf(targetGroup);
        }
        else
        { // Not grouped, so just find the meal
            var targetMeal = MealList.FirstOrDefault(ms => ms.IsForCurrentMeal);
            if (targetMeal is not null)
                targetIndex = MealList.IndexOf(targetMeal);
        }
        if (targetIndex >= 0)
            ScrollItemsTo(targetIndex, ScrollToPosition.Center, false); // Don't animate the initial scroll
    }

    [RelayCommand]
    private void ShowVenueDetails(MealSummaryGroup group)
    {
        if (group is null)
            return;
        Venue v = Venue.FindVenueByName(group.VenueName);
        if (v is not null)
        {
            SelectedGroup = group;
            App.PushAsync(Routes.VenueEditPage, "Venue", v);
        }
    }

    [RelayCommand]
    private void SelectGroup(MealSummaryGroup group)
    {
        if (group is null) return;
        if (SelectedGroup == group)
        {
            group.IsExpanded = false;
            SelectedGroup = null;
        }
        else
        {
            group.IsExpanded = true;
            SelectedGroup = group;
        }
    }

    [RelayCommand]
    private void SelectMeal(MealSummary ms)
    {
        if (ms is null) return;
        if (IsSelectableList)
        {
            ms.FileSelected = !ms.FileSelected;
            SelectedMealSummariesCount += (ms.FileSelected) ? 1 : -1;
        }
        else if (IsGrouped)
        {
            // find what group we are in and either deselect it if it is selected, or select it
            MealSummaryGroup thisGroup = MealSummaryGroups.FirstOrDefault(g => g.VenueName == ms.VenueName);
            SelectedGroup = SelectedGroup == thisGroup ? null : thisGroup;
        }
        else
            SelectedMealSummary = SelectedMealSummary == ms ? null : ms;
    }

    [RelayCommand]
    private void SelectNone()
    {
        IsSelectableList = true;
        foreach (var mealSummary in MealList.Where(ms => ms.FileSelected))
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
    #endregion
    /// <summary>
    /// Force future callers to reevaluate the list because we know it has changed
    /// </summary>
    public void InvalidateMealList()
    {
        MealList = null; // The list is not accurate any more
        MealSummaryGroups = null; // The groups are not accurate any more
        if (IsGrouped)
            OnPropertyChanged(nameof(MealSummaryGroups)); // Force the groups to be rebuilt
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

    /// <summary>
    /// Set to filter on a specific venue
    /// </summary>
    [ObservableProperty]
    public partial string FilteredVenueName { get; set; } = string.Empty;

    partial void OnFilteredVenueNameChanged(string value) => InvalidateMealList();
    #region Sorting
    public string SortOrderName => SortOrder == SortOrderType.byName ? "name" : SortOrder == SortOrderType.byDate ? "age" : SortOrder == SortOrderType.byDistance ? "distance" : "unknown";
    public enum SortOrderType { byDate, byDistance, byName };
    public void NextSortOrder()
    {
        if (SortOrder == Enum.GetValues<SortOrderType>().Max())
            SortOrder = Enum.GetValues<SortOrderType>().Min();
        else
            SortOrder++;
    }
    public SortOrderType SortOrder
    {
        get;
        set => SetProperty(ref field, value, () => { InvalidateMealList(); OnPropertyChanged(nameof(SortOrderName)); });
    } = SortOrderType.byDate;
    private string Sort
    {
        get => Enum.GetName<SortOrderType>(SortOrder);
        set
        {
            string sortRequest = Uri.UnescapeDataString(value ?? string.Empty);
            SortOrder = sortRequest.Equals("name") ? SortOrderType.byName : SortOrderType.byDate;
        }
    }
    #endregion
    [RelayCommand]
    private void ShowGroup(MealSummaryGroup group)
    {
        if (group is not null)
        {
            if (group.IsExpanded)
            {
                if (expandedGroup != null && expandedGroup != group)
                    expandedGroup.IsExpanded = false;
                expandedGroup = group;
                SelectedGroup = group;
            }
            else
            {
                expandedGroup = null; // Group has been collapsed, so there's no need to remember it
            }
        }
    }

    [RelayCommand]
    private async Task ShowMealsForVenue(string venueName) => await App.GoToAsync(Routes.MealListByAgePage,
            $"venue={Uri.EscapeDataString(venueName)}&ShowLocal={ShowLocalMeals}&ShowRemote={ShowRemoteMeals}");
    [RelayCommand]
    private void OnGroupExpanded(object o)
    {
        Utilities.DebugMsg($">>> MainViewModel.OnGroupExpanded({(o ?? "null")})");
        if (o is MealSummaryGroup group)
        {
            if (group.IsExpanded)
            {
                if (expandedGroup != null && expandedGroup != group)
                    expandedGroup.IsExpanded = false;
                expandedGroup = group;
                SelectedGroup = group;
            }
            else
            {
                expandedGroup = null; // Group has been collapsed, so there's no need to remember it
            }
        }
    }

    private MealSummaryGroup expandedGroup = null;

    [ObservableProperty]
    public partial MealSummaryGroup SelectedGroup { get; set; } = null;
    partial void OnSelectedGroupChanged(MealSummaryGroup oldValue, MealSummaryGroup newValue)
    {
        if (oldValue is not null && newValue is not null)
        {
            oldValue.IsExpanded = false;
        }
    }

    /// <summary>
    /// Show only venues if true, each bill otherwise.
    /// </summary>
    [ObservableProperty]
    public partial bool IsGrouped { get; set; } = false;
    partial void OnIsGroupedChanged(bool value) => InvalidateMealList(); // DeselectInvisibleMeals() is not needed here because the filtering code handles it

    #region Show/Hide Local/Remote
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WhereText))]
    public partial bool ShowLocalMeals { get; set; } = true;

    partial void OnShowLocalMealsChanged(bool value) { if (!value) DeselectInvisibleMeals(); InvalidateMealList(); }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WhereText))]
    public partial bool ShowRemoteMeals { get; set; } = false;

    partial void OnShowRemoteMealsChanged(bool value) { if (!value) DeselectInvisibleMeals(); InvalidateMealList(); }
    public string WhereText => ShowLocalMeals == ShowRemoteMeals ? null : ShowLocalMeals ? "local" : "remote";
    #endregion

    [ObservableProperty]
    public partial bool IsCloudAllowed { get; set; } = App.IsCloudAllowed;

    [ObservableProperty]
    public partial double Progress { get; set; } = 0;

    [ObservableProperty]
    public partial int ProgressLimit { get; set; } = 0;

    [ObservableProperty]
    public partial bool IsSelectableList { get; set; } = false;

    partial void OnIsSelectableListChanged(bool value)
    {
        if (IsSelectableList)
            MealCollectionMode = SelectionMode.None; // This is not a typo, we manage SelectedMealSummaries ourselves
        else
            MealCollectionMode = SelectionMode.Single;
        SetSelectedMealSummariesCountText();
    }

    [ObservableProperty]
    public partial MealSummary SelectedMealSummary { get; set; }
    public int SelectedMealSummariesCount
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                SetSelectedMealSummariesCountText();
            }
        }
    } = 0;
    public bool SetCount { get => false; set => SetSelectedMealSummariesCount(); }
    private void SetSelectedMealSummariesCount() => SelectedMealSummariesCount = MealList.Count(ms => ms.FileSelected);
    private void SetSelectedMealSummariesCountText() => SelectedMealSummariesCountText = SelectedMealSummariesCount > 0 & IsSelectableList ? SelectedMealSummariesCount.ToString() : null;

    [ObservableProperty]
    public partial string SelectedMealSummariesCountText { get; set; } = null;

    [ObservableProperty]
    public partial SelectionMode MealCollectionMode { get; set; } = SelectionMode.Single;

    [ObservableProperty]
    public partial bool IsMealListLoading { get; set; }

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
            // Local functions

            List<MealSummary> GetList()
            {
                IEnumerable<MealSummary> mealSummaries = ShowLocalMeals
                    ? (ShowRemoteMeals)
                        ? Meal.LocalMealList.Union(Meal.RemoteMealList).OrderByDescending(ms => ms.CreationTime) // merge the two lists
                        : Meal.LocalMealList
                    : ShowRemoteMeals ? Meal.RemoteMealList : [];
                if (!string.IsNullOrWhiteSpace(FilteredVenueName))
                    mealSummaries = mealSummaries.Where(ms => ms.VenueName.Equals(FilteredVenueName, StringComparison.OrdinalIgnoreCase));
                return [.. mealSummaries];
            }

            static IOrderedEnumerable<MealSummary> SortByDistance(IEnumerable<MealSummary> mealSummaries) => App.MyLocation is null
                    ? mealSummaries.OrderBy((ms) => ms.VenueName)
                    : mealSummaries.OrderBy((ms) => ms.Distance).ThenBy((ms) => ms.VenueName).ThenByDescending((ms) => ms.CreationTime);

            // Begin MealList 'get' code

            if (mealList is not null)
                return mealList; // There's a cached one, just use it
            List<MealSummary> theList = GetList();
            if (theList.Count <= 1)
            {
                // Handle the trivial cases of one or zero entries
                mealList = [.. theList]; // If there are one or zero meals, sort order and meal grouping are unnecessary
            }
            else // A nontrivial list
            {
                mealList = SortOrder switch
                {
                    SortOrderType.byName => [.. theList.OrderBy((ms) => ms.VenueName)],
                    SortOrderType.byDistance => [.. SortByDistance(theList)],
                    SortOrderType.byDate => [.. theList],
                    _ => throw new ArgumentOutOfRangeException(nameof(SortOrder), "Unknown sort order")
                };
            }
            SelectedMealSummariesCount = mealList.Count(ms => ms.FileSelected);

            string priorVenue = "";
            int priorDistance = 0;
            if (App.MyLocation is not null)
            { // Set the distance for each venue}
                foreach (MealSummary ms in mealList)
                {
                    if (ms.VenueName == priorVenue)
                        ms.Distance = priorDistance;
                    else
                    {
                        priorVenue = ms.VenueName;
                        Venue v = Venue.FindVenueByName(ms.VenueName);
                        priorDistance = ms.Distance = v is null ? Distances.Unknown : v.SimplifiedDistance;
                    }
                }
            }
            return mealList;
        }
        private set => SetProperty(ref mealList, value);
    }
    private bool UpsertIntoMealList(MealSummary ms) => SortOrder switch
    {
        SortOrderType.byDistance => mealList.Upsert(ms, MealSummary.CompareDistanceTo),
        SortOrderType.byName => mealList.Upsert(ms, MealSummary.CompareVenueTo),
        _ => mealList.Upsert(ms, MealSummary.CompareCreationTimeTo),
    };

    public ObservableCollection<MealSummaryGroup> MealSummaryGroups
    {
        get
        {
            // Local function
            static IEnumerable<MealSummaryGroup> SortByDistance(IEnumerable<MealSummaryGroup> mealSummaries) => App.MyLocation is null
                    ? mealSummaries.OrderBy((ms) => ms.VenueName)
                    : mealSummaries.OrderBy((ms) => ms.Distance).ThenBy((ms) => ms.VenueName).ThenByDescending((ms) => ms.CreationTime);

            if (field is not null)
                return field;
            if (!IsGrouped)
                return null;

            string workingVenueName = "";
            MealSummaryGroup mealSummaryGroup = new();
            List<MealSummaryGroup> Groups = [];
            foreach (MealSummary mealSummary in MealList.OrderBy(ms => ms.VenueName).ThenByDescending(ms => ms.CreationTime))
            {
                if (workingVenueName.Equals(mealSummary.VenueName))
                {
                    mealSummaryGroup.MealSummaries.Add(mealSummary);
                }
                else
                {
                    // The group changes, so store the current group and start a new one
                    if (mealSummaryGroup.Count > 0)
                    {
                        Groups.Add(mealSummaryGroup);
                        mealSummaryGroup.Count = mealSummaryGroup.MealSummaries.Count;
                    }
                    mealSummaryGroup = new(mealSummary);
                    workingVenueName = mealSummary.VenueName;
                }
                if (mealSummary.IsForCurrentMeal)
                    mealSummaryGroup.IsForCurrentMeal = true;
            }
            // Add the final group if there is one
            if (!string.IsNullOrWhiteSpace(mealSummaryGroup.VenueName))
            {
                Groups.Add(mealSummaryGroup);
                mealSummaryGroup.Count = mealSummaryGroup.MealSummaries.Count;
            }
            field = SortOrder switch
            {
                SortOrderType.byName => [.. Groups.OrderBy((g) => g.VenueName)],
                SortOrderType.byDistance => [.. SortByDistance(Groups)],
                SortOrderType.byDate => [.. Groups.OrderByDescending((g) => g.CreationTime)],
                _ => throw new ArgumentOutOfRangeException(nameof(SortOrder), "Unknown sort order")
            };
            return field;
        }
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(MealSummaryGroups));
            }
        }
    }
    /// <summary>
    /// Insert a  <see cref="MealSummaryGroup"/> in <see cref="MealSummaryGroups"/>, or move it if it is already there but should be 
    /// in a different place in the list. The list is ordered based on a compare function.
    /// </summary>
    /// <param name="g">The <see cref="MealSummaryGroup"/> we're working with</param>
    /// <returns>True if the group was moved or inserted, false if nothing changed></returns>
    private bool UpsertIntoMealSummaryGroupList(MealSummaryGroup g) => MealSummaryGroups.Upsert(g,
        SortOrder switch
        {
            SortOrderType.byDistance => MealSummaryGroup.CompareDistanceTo,
            SortOrderType.byName => MealSummaryGroup.CompareVenueTo,
            _ => MealSummaryGroup.CompareCreationTimeTo
        });

    #region Scrolling Item list
    /// On the face of it scrolling (probably of a CollectionView) is simple, in practice scroll notification it 
    /// differs somewhat on Windows and Android but the key is if you're scrolling a long way, don't animate it 
    /// because that slows things down a lot.
    /// The other major wrinkle is that we have to deal with the fact that the CollectionView doesn't
    /// support scrolling notifications in a consistent manner across platforms and there is no way to
    /// reliably determine when scrolling has ended, so we do it with a timer.
    private int LastItemIndex => (IsGrouped && MealSummaryGroups is not null) ? MealSummaryGroups.Count - 1 : (MealList is not null) ? MealList.Count - 1 : -1;

    [ObservableProperty]
    public partial bool IsSwipeUpAllowed { get; set; }

    [ObservableProperty]
    public partial bool IsSwipeDownAllowed { get; set; }

    /// <summary>
    /// The index of the first visible item on the page as set by a UI event (probably OnCollectionViewScrolled)
    /// </summary>
    [ObservableProperty]
    public partial int FirstVisibleItemIndex { get; set; }

    partial void OnFirstVisibleItemIndexChanged(int value)
    {
        IsSwipeDownAllowed = value > 0;
        //IsMealListScrollingFar = true; // Enable to turn on manual scroll detection
        scrollEndTimer.Change(200, 0); // Notify end of scroll if we do not see FirstVisibleItemIndex change for a while
    }

    /// <summary>
    /// The index of the last visible item on the page as set by a UI event (probably OnCollectionViewScrolled)
    /// </summary>
    [ObservableProperty]
    public partial int LastVisibleItemIndex { get; set; }

    partial void OnLastVisibleItemIndexChanged(int value)
    {
        IsSwipeUpAllowed = value > 0 && value < LastItemIndex;
    }

    private readonly Timer scrollEndTimer; // fires when we think scrolling has ended

    [ObservableProperty]
    public partial bool IsMealListScrollingFar { get; private set; } = false;

    /// <summary>
    /// Scroll the control displaying the items to a particular item index.
    /// The item is scrolled to the top or bottom of the control depending on the value of itemPositionRelativeToEnd.
    /// </summary>
    /// <param name="index">The index of the item we are scrolling to</param>
    /// <param name="scrollToPosition">Where on the page should the item be shown</param>
    /// <param name="animate">Should the scrolling be animated</param>
    public delegate void ScrollItemsToDelegate(int index, ScrollToPosition scrollToPosition, bool animate = true);

    /// <summary>
    /// A <see cref="ScrollItemsToDelegate"/> function which is called to scroll the list of items, provided by the page.
    /// </summary>
    public ScrollItemsToDelegate ScrollItemsTo = null;

    /// <summary>
    /// <para>Scroll back and forth through the list of items. We can scroll to the first or last item in the list
    /// as well as scrolling a page at a time to the first or last visible item. When we scroll to the beginning or 
    /// end of the list and that's more than a few (30 at this point) records we disable the collection view so that
    /// we don't have to keep updating the UI. This is a performance optimization but also looks better to the user.
    /// </para><para>
    /// The scroll direction is the direction the items appear to move, not the direction the window appears to move.
    /// </para><para>
    /// The algorithmic complexity comes from the fact that ScrollItemsTo is a fire-and-forget function which scrolls the
    /// control incrementally and we don't want to continue until it is done so we end up using a timer to do that.
    /// </para>
    /// </summary>
    /// <param name="whereTo">A string containing Up/Down/Start/End describing the place to scroll to or the direction to scroll</param>
    [RelayCommand]
    private async Task ScrollItems(string whereTo)
    {
        if (FirstVisibleItemIndex == LastVisibleItemIndex // There's only one item
            || ScrollItemsTo is null // We were not passed a ScrollTo function
            || LastItemIndex <= 1 // There are one or zero items
            || MealList is null // We don't even have a list
            || (IsGrouped && MealSummaryGroups is null)) // We don't have a list of groups
            return;
        try
        {
            switch (whereTo)
            {
                case "Up": if (LastVisibleItemIndex < LastItemIndex) await ScrollItemsImpl(LastVisibleItemIndex, ScrollToPosition.Start); break;
                case "Down": if (FirstVisibleItemIndex > 0) await ScrollItemsImpl(FirstVisibleItemIndex, ScrollToPosition.End); break;
                case "End": if (LastVisibleItemIndex < LastItemIndex) { await ScrollItemsImpl(LastItemIndex, ScrollToPosition.End); } break;
                case "Start": if (FirstVisibleItemIndex > 0) { await ScrollItemsImpl(0, ScrollToPosition.Start); } break;
                default: break;
            }
        }
        catch (Exception ex)
        {
            ex.ReportCrash("fault attempting to scroll");
            // Do nothing, we do not really care if a scroll attempt fails
        }
    }
    private async Task ScrollItemsImpl(int scrollToIndex, ScrollToPosition scrollToPosition)
    {
        const int manyItems = 30; // 30 items is our definition of scrolling a long way
        bool scrollingDown = scrollToPosition == ScrollToPosition.Start;
        int scrollDistance = Math.Abs(scrollToIndex - (scrollingDown ? LastVisibleItemIndex : FirstVisibleItemIndex)); // How many items we'll be scrolling past
        if (scrollDistance >= manyItems)
            IsMealListScrollingFar = true;
        await Task.Yield(); // allow the UI to update before we call ScrollItemsTo
        ScrollItemsTo(scrollToIndex, scrollToPosition, scrollDistance < manyItems); // For a short scroll it's ok to animate, but it's slow so we don't use it for long scrolls  
    }
    #endregion
    // IQueryAttributable interface
    // This handles query parameters
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        static bool boolValue(object value) => value is string s && bool.TryParse(s, out bool parsedBool) ? parsedBool : value is bool b && b;

        #region Leave alone if not set
        if (query.TryGetValue("sort", out var sortValue))
            Sort = sortValue?.ToString();
        if (query.TryGetValue("ShowLocal", out var showLocalValue))
            ShowLocalMeals = boolValue(showLocalValue);
        if (query.TryGetValue("ShowRemote", out var showRemoteValue))
            ShowRemoteMeals = boolValue(showRemoteValue);
        #endregion
        #region Default values if not set
        IsSelectableList = query.TryGetValue("IsSelectableList", out var selectableValue) && boolValue(selectableValue);
        IsGrouped = query.TryGetValue("grouped", out var groupValue) && boolValue(groupValue);
        SetCount = query.TryGetValue("count", out var countValue) && boolValue(countValue);
        FilteredVenueName = query.TryGetValue("venue", out var venueValue) && venueValue is string venueName ? Uri.UnescapeDataString(venueName).ToString() : null;
        #endregion
    }
}
