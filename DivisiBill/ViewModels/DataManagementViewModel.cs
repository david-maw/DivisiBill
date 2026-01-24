#nullable enable

using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Models;
using DivisiBill.Services;

namespace DivisiBill.ViewModels;

internal partial class DataManagementViewModel : ObservableObject
{

    public void OnNavigatedTo()
    {
        if (SelectedArchive is null)
        {
            // Set dates based on the current list of local meals, which may have changed while we were away
            StartDate = EarliestStartDate = Meal.LocalMealList?.LastOrDefault()?.CreationTime ?? DateTime.Now;
            FinishDate = LatestFinishDate = Meal.LocalMealList?.FirstOrDefault()?.CreationTime ?? DateTime.Now;
        }
    }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly DateTime nextTime = DateTime.MinValue;

    [ObservableProperty]
    public partial Archive? SelectedArchive { get; private set; }

    [ObservableProperty]
    public partial int SelectedMealsCount { get; private set; } = 0;

    /// <summary>
    /// Selects all but the latest meal for each venue from local storage and navigates to the meal list page with specific query parameters.
    /// </summary>
    /// <returns>Returns a Task representing the asynchronous operation.</returns>
    [RelayCommand]
    private static async Task SelectOlder()
    {
        Models.Meal.SelectOlder();
        await App.GoToAsync(Routes.MealListByAgePage + "?IsSelectableList=true&count=true&ShowLocal=true&ShowRemote=false");
    }

    /// <summary>
    /// Selects meals that are remote but not local and sets a busy indicator if it takes more than 500 ms to figure
    /// out the remote list. If the remote list is empty or remote access is not available, a message is displayed.
    /// </summary>
    [RelayCommand]
    private async Task SelectDownloadable()
    {
        // Ensure that cloud access is allowed and usable
        if (!App.IsCloudAllowed)
        {
            if (!App.Settings.IsCloudAccessAllowed)
                await Utilities.ShowAppSnackBarAsync("Cloud access is not enabled in settings");
            else
                await Utilities.ShowAppSnackBarAsync("Cloud is currently inaccessible");
            return;
        }
        // get the list of remotely stored meals
        Task<bool> task = Meal.GetRemoteMealListAsync();
        try
        {
            Task whichTask = await Task.WhenAny(Task.Delay(500), task);
            if (whichTask != task)
                IsBusy = true;
            await task;
        }
        finally
        {
            if (IsBusy)
            {
                await Task.Delay(1000);
                IsBusy = false;
            }
        }
        // Make sure it worked correctly
        if (!task.Result)
        {
            await Utilities.ShowAppSnackBarAsync("Remote bills are not currently available");
            return;
        }
        if (Meal.RemoteMealList.Count == 0)
            await Utilities.ShowAppSnackBarAsync("There are no remote bills");
        else
        {
            var localMealDict = Meal.LocalMealList.ToDictionary(ms => ms.Id);
            bool foundOne = false;
            foreach (MealSummary? mealSummary in Meal.RemoteMealList.Where(ms => !localMealDict.ContainsKey(ms.Id)))
            {
                mealSummary.FileSelected = true;
                foundOne = true;
            }
            if (foundOne)
                await App.GoToAsync(Routes.MealListByAgePage + "?IsSelectableList=true&count=true&ShowLocal=true&ShowRemote=true");
            else
                await Utilities.ShowAppSnackBarAsync($"All {Meal.RemoteMealList.Count} remote bills are already downloaded");
        }
    }

    /// <summary>
    /// Writes an archive of the data to a file, either to disk or to a shared location (an app in Android), depending on the values 
    /// of <see cref="ArchiveShare"/> or <see cref="ArchiveToDisk"/>.
    /// </summary>
    [RelayCommand]
    public async Task ArchiveAsync()
    {
        if (OnlySelectedMeals && !Meal.LocalMealList.Any(ms => ms.FileSelected))
        {
            await Utilities.DisplayAlertAsync("Archiving Error", "No bills are selected");
            return;
        }
        // Make a list of meals by looping through list of local mealSummaries and creating a meal from selected ones
        List<Meal> toArchive = [.. Meal.LocalMealList
            .Where(ms => // A meal that is already selected (if we are selecting) and within date range if there is one
                (!OnlySelectedMeals || ms.FileSelected) &&
                DateOnly.FromDateTime(ms.CreationTime) >= DateOnly.FromDateTime(StartDate) &&
                DateOnly.FromDateTime(ms.CreationTime) <= DateOnly.FromDateTime(FinishDate)
            )
            .OrderByDescending(ms => ms.CreationTime)
            .Select(ms => Meal.LoadFromFile(ms))];
        Archive archive = new(toArchive, OnlyRelated);
        archive.UserSettings.BillsFromDate = StartDate.ToShortDateString();
        archive.UserSettings.BillsToDate = FinishDate.ToShortDateString();
        if (archive.AllMeals.Count == 0)
        {
            await Utilities.DisplayAlertAsync("Archiving Error", "No bills meet the archive criteria");
            return;
        }
        string zipFullName = archive.ZipAsync(SaveImages);
        if (string.IsNullOrWhiteSpace(zipFullName) || !File.Exists(zipFullName))
        {
            await Utilities.ShowAppSnackBarAsync("Archive Zip File Creation Failed");
            return;
        }
        // At this point we have a zip archive file on disk containing a single XML file containing the archive data
        try
        {
            if (ArchiveShare)
            {

                Task sharing = Share.RequestAsync(new ShareFileRequest
                {
                    Title = "DivisiBill Archive",
                    File = new ShareFile(zipFullName)
                });
                await sharing;
                if (sharing.IsCompletedSuccessfully)
                {
                    // We want to delete the archive file only after the sharing process is done with it
                    // but there's no easy way to tell when that is, so just leave it in the temp folder for now
                    // File.Delete(zipFilePath);
                    await Utilities.ShowAppSnackBarAsync("Archive Sharing Initiated");
                }
                else
                    await Utilities.ShowAppSnackBarAsync("Archive Sharing Failed");
            }
            else if (ArchiveToDisk)
            {
                FileSaverResult? fileSaverResult = null;
                using (Stream s = new FileStream(zipFullName, FileMode.Open, FileAccess.Read))
                {
                    fileSaverResult = await FileSaver.Default.SaveAsync(zipFullName, s);
                }
                if (fileSaverResult.IsSuccessful)
                {
                    File.Delete(zipFullName);
                    await Utilities.ShowAppSnackBarAsync("Archive to disk completed successfully");
                }
                else
                    await Utilities.ShowAppSnackBarAsync("Archive Failed");
            }
            else
                await Utilities.ShowAppSnackBarAsync("Archive Stream Creation Failed");
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
        }
    }

    /// <summary>
    /// Command that lets the user pick an archive file (zip or xml). This command deserializes the archive into SelectedArchive.
    /// It does not extract images; images are restored later by RestoreArchiveAsync and only for meals actually restored.
    /// </summary>
    [RelayCommand]
    public async Task SelectArchiveAsync()
    {
        try
        {
            FileResult? result = await FilePicker.PickAsync(new PickOptions()
            {
                PickerTitle = "Please select an archive file",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                        { DevicePlatform.Android, [ "text/xml", "application/zip" ] },
                        { DevicePlatform.WinUI, [ ".xml", "*.zip" ] },
                }),
            });
            if (result is not null)
            {
                IsBusy = true;
                string archiveName = result.FileName;
                Utilities.DebugMsg($"In SelectArchiveAsync: file name {archiveName}");

                // Uncomment these lines to more easily test the stream handling page
                // App.Current.IntentQueue.Enqueue(new Services.StreamRequest(File.OpenRead(result.FullPath), result.ContentType));
                // await Shell.Current.Navigation.PushModalAsync(new Views.RestorePage());

                (Archive? archive, string message) = await Archive.DeserializeAnyAsync(result.FullPath);
                if (archive is null)
                {
                    IsBusy = false;
                    if (string.IsNullOrWhiteSpace(message))
                        message = "Archive deserialization failed: Unknown error";
                    await Utilities.ShowAppSnackBarAsync(message);
                    return;
                }

                // Set dates based on all the meals in the archive
                DateTime NewStartDate = EarliestStartDate = archive.AllMeals?.LastOrDefault()?.CreationTime ?? DateTime.Now;
                DateTime NewFinishDate = LatestFinishDate = archive.AllMeals?.FirstOrDefault()?.CreationTime ?? DateTime.Now;

                SelectedArchive = archive;
                SelectedMealsCount = archive.AllMeals is null ? 0 : archive.AllMeals.Count;
                if (SelectedMealsCount > 0)
                {
                    StartDate = NewStartDate; // Note that setting this date will change the contents of SelectedMeals
                    FinishDate = NewFinishDate; // Note that setting this date will change the contents of SelectedMeals
                }
            }
            else
            {
                SelectedArchive = null;
                // Set dates based on all the local meals (initially all are selected)
                StartDate = EarliestStartDate = Meal.LocalMealList?.LastOrDefault()?.CreationTime ?? DateTime.Now;
                FinishDate = LatestFinishDate = Meal.LocalMealList?.FirstOrDefault()?.CreationTime ?? DateTime.Now;
                Utilities.DebugMsg($"In SelectArchiveAsync: returned file name was null");
            }
        }
        catch (Exception ex)
        {
            IsBusy = false;
            ex.ReportCrash();
            // The user canceled or something went wrong
            await Utilities.ShowAppSnackBarAsync("Restore Faulted, Archive was unusable");

        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Command to restore the previously selected archive (SelectedArchive). This restores archive items and selectively
    /// extracts images from the original zip only for the meals that were restored.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRestoreArchive))]
    public async Task RestoreArchiveAsync()
    {
        if (SelectedArchive is null)
        {
            await Utilities.ShowAppSnackBarAsync("No archive selected to restore");
            return;
        }

        IsBusy = true;
        await Task.Yield(); // Give the UI a chance to update - this seems to sometimes be necessary on Android
        try
        {
            Archive archive = SelectedArchive;

            // Apply user settings from the archive (if present)
            if (archive.UserSettings is not null)
            {
                if (App.Current.Resources["MealViewModel"] is MealViewModel mvm)
                {
                    mvm.DefaultTipRate = archive.UserSettings.DefaultTipRate;
                    mvm.DefaultTaxRate = archive.UserSettings.DefaultTaxRate;
                    mvm.DefaultTipOnTax = archive.UserSettings.DefaultTipOnTax;
                    mvm.DefaultTaxOnCoupon = archive.UserSettings.DefaultTaxOnCoupon;
                }

                App.Settings.ShowLineItemsHint = archive.UserSettings.ShowLineItemsHint;
                App.Settings.ShowTotalsHint = archive.UserSettings.ShowTotalsHint;
                App.Settings.ShowVenuesHint = archive.UserSettings.ShowVenuesHint;
                App.Settings.ShowPeopleHint = archive.UserSettings.ShowPeopleHint;

                if (archive.UserSettings.FakeLocation is not null)
                {
                    App.FakeLocation = archive.UserSettings.FakeLocation;
                    if (App.UseFakeLocation) // The location was already fake
                    {
                        await App.RefreshLocationAsync(); // Start using the new fake location
                        await Utilities.ShowAppSnackBarAsync("Fake location changed");
                    }
                }
            }

            // Restore the data items
            (bool restoreWorked, string restoreFailureText) = await archive.RestoreAnyAsync(DeleteBeforeRestore, OverwriteDuplicates, OnlyRelated);

            if (restoreWorked)
            {
                // Navigate to meal list after restore
                await App.GoToAsync(Routes.MealListByAgePage);

                // Clear selected archive after restore
                SelectedArchive = null;
                SelectedMealsCount = 0;
            }
            else if (restoreFailureText != null)
                await Utilities.ShowAppSnackBarAsync($"Restore completed with issues: {restoreFailureText}");
            else
                await Utilities.ShowAppSnackBarAsync("Restore failed");
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            await Utilities.ShowAppSnackBarAsync("Restore Faulted, Archive was unusable");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRestoreArchive() => SelectedMealsCount > 0;

    /// <summary>
    /// Indicates whether an archive is shared (the other alternative is to store it to disk). The default value is true.
    /// </summary>
    [ObservableProperty]
    public partial bool ArchiveShare { get; set; } = true;

    /// <summary>
    /// Indicates whether to archive data to disk (the other alternative is to share it). The default value is false.
    /// </summary>
    [ObservableProperty]
    public partial bool ArchiveToDisk { get; set; } = false;

    /// <summary>
    /// Indicates whether to save images during archiving operations. Defaults to false.
    /// </summary>
    [ObservableProperty]
    public partial bool SaveImages { get; set; } = true;

    /// <summary>
    /// Indicates whether all meals or only selected meals are candidates for archiving. Defaults to false.
    /// </summary>
    [ObservableProperty]
    public partial bool OnlySelectedMeals { get; set; } = false;

    /// <summary>
    /// Indicates whether only items referred to by meals being archived or restored should themselves be archived or restored.
    /// Initialized to false by default.
    /// </summary>
    [ObservableProperty]
    public partial bool OnlyRelated { get; set; } = false;

    /// <summary>
    /// Indicates whether to delete all items before commencing a restore operation. Defaults to false.
    /// </summary>
    [ObservableProperty]
    public partial bool DeleteBeforeRestore { get; set; } = false;

    [ObservableProperty]
    public partial bool OverwriteDuplicates { get; set; } = false;

    /// There's some strangeness below of DateOnly vs. DateTime, FinishDate and StartDate ought to be type DateOnly but 
    /// DatePicker controls do not work with the DateOnly type. See https://github.com/dotnet/maui/issues/20438 and
    /// https://github.com/dotnet/maui/issues/1100 for more information. To summarize, that's how it works until #1100 is implemented.

    [ObservableProperty]

    public partial DateTime EarliestStartDate { get; set; }

    [ObservableProperty]

    public partial DateTime LatestFinishDate { get; set; }


    /// <summary>
    /// Get or set the earliest date in the range of bills which should be archived or restored
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedArchive))]

    public partial DateTime StartDate { get; set; } = Archive.EarliestDateAllowed;

    partial void OnStartDateChanged(DateTime value)
    {
        if (StartDate > FinishDate)
            FinishDate = StartDate;
        if (SelectedArchive is not null)
            SelectedMealsCount = SelectedArchive.SetDateRange(DateOnly.FromDateTime(StartDate), DateOnly.FromDateTime(FinishDate));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedArchive))]
    public partial DateTime FinishDate { get; set; } = DateTime.Now.Date;
    partial void OnFinishDateChanged(DateTime value)
    {
        if (FinishDate < StartDate)
            StartDate = FinishDate;
        if (SelectedArchive is not null)
            SelectedMealsCount = SelectedArchive.SetDateRange(DateOnly.FromDateTime(StartDate), DateOnly.FromDateTime(FinishDate));
    }

    /// <summary>
    /// Password used for archiving/restoring keys.
    /// </summary>
    [ObservableProperty]
    public partial string KeyArchivePassword { get; set; } = string.Empty;

    partial void OnKeyArchivePasswordChanged(string value)
    {
        ArchiveKeysCommand.NotifyCanExecuteChanged();
        RestoreKeysCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Command to archive keys to a selected file using CryptManager.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanArchiveOrRestoreKeys))]
    private async Task ArchiveKeysAsync(string commandParameter)
    {
        string archiveName = "KeysArchive-" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".zip";
        try
        {
            using MemoryStream stream = new();
            int keysArchived = await CryptManager.ArchivePrivateKeysToZipAsync(KeyArchivePassword, stream);
            if (keysArchived == 0)
                await Utilities.ShowAppSnackBarAsync("No keys available to archive");
            else
            {
                stream.Position = 0; // Reset stream position before saving
                if (commandParameter is null || !commandParameter.Equals("share"))
                {
                    FileSaverResult result = await FileSaver.Default.SaveAsync(archiveName, stream);
                    if (result.IsSuccessful)
                        await Utilities.ShowAppSnackBarAsync($"{keysArchived} key(s) Archived to {result.FilePath}");
                    else
                        await Utilities.ShowAppSnackBarAsync("Failed to select file for key archive");
                }
                else
                {
                    // Save to a temporary file for sharing
                    string tempArchiveFilePath = Path.Combine(FileSystem.CacheDirectory, archiveName);
                    using (FileStream fileStream = new(tempArchiveFilePath, FileMode.Create, FileAccess.Write))
                    {
                        await stream.CopyToAsync(fileStream);
                    }
                    Task shareFileInitiationTask = Share.RequestAsync(new ShareFileRequest
                    {
                        Title = archiveName,
                        File = new ShareFile(tempArchiveFilePath)
                    });
                    await shareFileInitiationTask;
                    if (shareFileInitiationTask.IsCompletedSuccessfully)
                    {
                        // We want to delete the archive file only after the sharing process is done with it
                        // but there's no easy way to tell when that is, so just leave it in the temp folder for now
                        // File.Delete(tempArchiveFilePath); 
                        await Utilities.ShowAppSnackBarAsync($"Key archive with {keysArchived} key(s) sharing initiated");
                    }
                    else
                        await Utilities.ShowAppSnackBarAsync("Key archive sharing failed");
                }
            }
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            await Utilities.ShowAppSnackBarAsync("Fault during key archiving");
        }
    }

    /// <summary>
    /// Command to restore keys from a selected file using CryptManager.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanArchiveOrRestoreKeys))]
    private async Task RestoreKeysAsync()
    {
        FileResult? result = await FilePicker.PickAsync(new PickOptions
        {
            PickerTitle = "Select key archive file",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, [ "application/zip" ] },
                { DevicePlatform.WinUI, [ ".zip" ] },
            }),
        });

        if (result is null)
        {
            await Utilities.ShowAppSnackBarAsync("No file selected for key restore.");
            return;
        }

        try
        {
            using Stream stream = await result.OpenReadAsync();
            (int restoredKeys, int totalKeys) = await CryptManager.RestorePrivateKeysFromZipAsync(KeyArchivePassword, stream);
            if (restoredKeys == totalKeys)
                await Utilities.ShowAppSnackBarAsync($"{restoredKeys} keys restored successfully.");
            else
                await Utilities.ShowAppSnackBarAsync($"{restoredKeys} of {totalKeys} keys restored successfully.");
        }
        catch (InvalidDataException ex)
        {
            string details = ex.Message;
            if (details is not null and "RSA fingerprint mismatch.")
                details = "Incorrect password.";
            await Utilities.ShowAppSnackBarAsync($"Key restore error: {details}");
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            await Utilities.ShowAppSnackBarAsync("Fault during key restore.");
        }
    }

    private bool CanArchiveOrRestoreKeys() => !string.IsNullOrWhiteSpace(KeyArchivePassword);

    [RelayCommand]
    private static async Task ClearCloudAsync()
    {
        if (App.IsCloudAllowed) // Ensure that cloud access is allowed and usable
        {
            await RemoteWs.DeleteAllImagesAsync();
            await RemoteWs.DeleteAllItemsAsync(RemoteWs.MealTypeName);
            await RemoteWs.DeleteAllItemsAsync(RemoteWs.PersonListTypeName);
            await RemoteWs.DeleteAllItemsAsync(RemoteWs.VenueListTypeName);
        }
        else
        {
            if (!App.Settings.IsCloudAccessAllowed)
                await Utilities.ShowAppSnackBarAsync("Cloud access is not enabled in settings");
            else
                await Utilities.ShowAppSnackBarAsync("Cloud is currently inaccessible");
            return;
        }
    }
}
