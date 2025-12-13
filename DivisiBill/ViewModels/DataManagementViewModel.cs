#nullable enable

using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Models;
using DivisiBill.Services;
using System.IO.Compression;

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

    // Store the archive selected by SelectArchiveAsync
    private Archive? selectedArchive = null;
    public Archive? SelectedArchive
    {
        get => selectedArchive;
        set
        {
            // Use SetProperty to raise PropertyChanged
            if (SetProperty(ref selectedArchive, value))
            {
                // Notify the generated command that can-execute may have changed
                RestoreArchiveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    // Path to the zip file selected (if any). Used later to extract images selectively during restore.
    private string? selectedArchiveZipPath = null;

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
            foreach (var mealSummary in Meal.RemoteMealList.Where(ms => !localMealDict.ContainsKey(ms.Id)))
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
        Archive archive = new(
            DateOnly.FromDateTime(StartDate),
            DateOnly.FromDateTime(FinishDate),
            OnlyRelated, OnlySelectedMeals);
        if (archive.Meals.Count == 0)
        {
            await Utilities.DisplayAlertAsync("Archiving Error", "No bills meet the archive criteria");
            return;
        }

        string xmlFileName = "DivisiBill" + archive.TimeName + ".xml";
        string xmlFilePath = Path.Combine(FileSystem.CacheDirectory, xmlFileName);
        string zipFilePath = Path.ChangeExtension(xmlFilePath, ".zip");
        try
        {
            using (Stream s = new FileStream(xmlFilePath, FileMode.OpenOrCreate))
            {
                s.SetLength(0); // Clear the file if it exists
                archive.AsXmlStream(s);
                s.Flush(); // Ensure the stream is written to disk before zipping
            }
            using (ZipArchive archiveZip = ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
            {
                archiveZip.CreateEntryFromFile(xmlFilePath, xmlFileName);
                File.Delete(xmlFilePath); // Delete the XML file after zipping
                Utilities.DebugMsg($"In {nameof(ArchiveAsync)}: created zip archive {zipFilePath} containing {xmlFileName}");
                if (SaveImages)
                {
                    // Save bill images if requested
                    foreach (var meal in archive.Meals.Where(m => m.HasImage && File.Exists(m.ImagePath)))
                    {
                        archiveZip.CreateEntryFromFile(meal.ImagePath, meal.ImageName);
                        Utilities.DebugMsg($"In {nameof(ArchiveAsync)}: added image {meal.ImageName} to zip archive");
                    }
                }
            }
            // At this point we have a zip archive file on disk containing a single XML file containing the archive data
            if (ArchiveShare)
            {

                Task sharing = Share.RequestAsync(new ShareFileRequest
                {
                    Title = "Archive " + xmlFileName,
                    File = new ShareFile(zipFilePath)
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
                using (Stream s = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read))
                { fileSaverResult = await FileSaver.Default.SaveAsync(Path.ChangeExtension(xmlFileName, ".zip"), s); }
                if (fileSaverResult.IsSuccessful)
                {
                    File.Delete(zipFilePath); 
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
        ZipArchive? zipArchive = null; // The zip archive if a zip was selected
        Stream? archiveStream = null; // The stream containing archived data (the XML entry or the xml file stream)
        try
        {
            var result = await FilePicker.PickAsync(new PickOptions()
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
                Utilities.DebugMsg($"In {nameof(SelectArchiveAsync)}: file name {archiveName}");
                if (Path.GetExtension(archiveName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        zipArchive = ZipFile.OpenRead(result.FullPath);
                        Utilities.DebugMsg($"In {nameof(SelectArchiveAsync)}: opened zip archive {archiveName}");
                    }
                    catch (Exception ex)
                    {
                        ex.ReportCrash();
                        await Utilities.ShowAppSnackBarAsync($"In {nameof(SelectArchiveAsync)}: Failed to open archive file");
                        return;
                    }
                    if (zipArchive is not null)
                    {
                        // Find the first XML file in the zip archive and assume it is an archive file
                        ZipArchiveEntry? zipArchiveEntry = zipArchive.Entries.Where(zAE => Path.GetExtension(zAE.Name).Equals(".xml", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                        if (zipArchiveEntry is not null)
                        {
                            archiveName = zipArchiveEntry.Name;
                            archiveStream = zipArchiveEntry.Open();
                        }
                        else
                        {
                            await Utilities.ShowAppSnackBarAsync($"In {nameof(SelectArchiveAsync)}: zip file does not contain a DivisiBill archive");
                            return;
                        }
                        // We do not extract images here; image extraction will be performed later during restore for only the meals that were restored.
                    }
                    else
                        await Utilities.ShowAppSnackBarAsync("Archive file is not a valid zip file");
                }
                else if (Path.GetExtension(archiveName).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    archiveStream = await result.OpenReadAsync();
                    selectedArchiveZipPath = null;
                }
                else
                    await Utilities.ShowAppSnackBarAsync("Archive file must be a .zip or .xml file");

                // By this point we have an archive name and a stream to the archive (XML content)
                if (archiveStream is not null)
                {
                    Archive? archive = DeserializeArchiveFromStream(archiveStream, archiveName);
                    if (archive is null)
                    {
                        await Utilities.ShowAppSnackBarAsync("Failed to deserialize archive");
                        return;
                    }

                    // Set dates based on all the meals in the archive (initially all are selected)
                    DateTime NewStartDate = EarliestStartDate = archive.SelectedMeals?.LastOrDefault()?.CreationTime ?? DateTime.Now;
                    DateTime NewFinishDate = LatestFinishDate = archive.SelectedMeals?.FirstOrDefault()?.CreationTime ?? DateTime.Now;

                    SelectedArchive = archive;
                    if (archive.SelectedMeals is not null && archive.SelectedMeals.Count > 0)
                    {
                        StartDate = NewStartDate; // Note that setting this date will change the contents of SelectedMeals
                        FinishDate = NewFinishDate; // Note that setting this date will change the contents of SelectedMeals
                    }

                    // If the original selection was a zip file record its path so images can be selectively extracted during restore later
                    selectedArchiveZipPath = (zipArchive is not null) ? result.FullPath : null;
                }
                else
                    Utilities.DebugMsg($"In {nameof(SelectArchiveAsync)}: no archive stream was found");
            }
            else
            {
                SelectedArchive = null;
                selectedArchiveZipPath = null;
                // Set dates based on all the local meals (initially all are selected)
                StartDate = EarliestStartDate = Meal.LocalMealList?.LastOrDefault()?.CreationTime ?? DateTime.Now;
                FinishDate = LatestFinishDate = Meal.LocalMealList?.FirstOrDefault()?.CreationTime ?? DateTime.Now;
                Utilities.DebugMsg($"In {nameof(SelectArchiveAsync)}: returned file name was null");
            }
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            // The user canceled or something went wrong
            await Utilities.ShowAppSnackBarAsync("Restore Faulted, Archive was unusable");

        }
        finally
        {
            archiveStream?.Dispose();
            zipArchive?.Dispose();
            IsBusy = false;
        }
    }

    /// <summary>
    /// Deserialize the provided XML stream into an Archive (or single-item Archive) but do not perform any restore actions.
    /// </summary>
    private Archive? DeserializeArchiveFromStream(Stream archiveStream, string archiveName)
    {
        try
        {
            // Reset stream position if possible
            if (archiveStream.CanSeek)
                archiveStream.Position = 0;

            Archive? archive = null;
            // For convenience we allow individual files to be deserialized 
            if (archiveName.StartsWith("Venues"))
            {
                List<Venue> vl = Venue.DeserializeList(archiveStream);
                if (vl is not null)
                    archive = new Archive() { Venues = vl };
                else
                    Utilities.DebugMsg($"In DeserializeArchiveFromStream, {archiveName} Venue.DeserializeList returned null");
            }
            else if (archiveName.StartsWith("People"))
            {
                List<Person> pl = Person.DeserializeList(archiveStream);
                if (pl is not null)
                    archive = new Archive() { Persons = pl };
                else
                    Utilities.DebugMsg($"In DeserializeArchiveFromStream, {archiveName} Person.DeserializeList returned null");
            }
            else if (Utilities.TryDateTimeFromName(archiveName, out _)) // Serialized Meal name format
            {
                Meal m = Meal.LoadFromStream(archiveStream);
                if (m is not null)
                    archive = new Archive() { Meals = new List<Meal>() { { m } } };
                else
                    Utilities.DebugMsg($"In DeserializeArchiveFromStream, {archiveName} Meal.LoadFromStream returned null");
            }
            else // Assume it is an archive
            {
                archive = Archive.FromStream(archiveStream);
            }

            // Some old archives are out of order so sort the list just in case
            if (archive?.Meals is not null)
                archive.SelectedMeals = archive.Meals.OrderByDescending(m => m.CreationTime).ToList();

            return archive;
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            return null;
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
            var archive = SelectedArchive;

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
            await archive.RestoreAsync(DeleteBeforeRestore, OverwriteDuplicates, OnlyRelated);

            // If the archive was a zip and contains images, selectively extract images belonging to meals being restored
            if (!string.IsNullOrWhiteSpace(selectedArchiveZipPath) && File.Exists(selectedArchiveZipPath))
            {
                try
                {
                    // Open the archive and put the entries in a dictionary indexed by name
                    using var zip = ZipFile.OpenRead(selectedArchiveZipPath);
                    Dictionary<string, ZipArchiveEntry> zippedImages = new();
                    foreach (var entry in zip.Entries) // mostly image files though the archive XML will be in there too
                        zippedImages[entry.Name] = entry;

                    if (DeleteBeforeRestore)
                        Meal.PermanentlyDeleteAllLocalImages();

                    // Iterate through the meals being restored that also have images present in the zip
                    foreach (var meal in archive.SelectedMeals.Where(m => zippedImages.ContainsKey(m.ImageName)))
                    {
                        // Find corresponding local meal by ImageName so we can update it later
                        var localMealSummary = Meal.LocalMealList.FirstOrDefault(lm => lm.CreationTime == meal.CreationTime);
                        if (localMealSummary is null)
                        {
                            Utilities.DebugMsg($"Restored Meal corresponding to {meal.ImageName} is missing");
                            continue; // meal was not present locally, which is weird, it should have just been restored
                        }

                        // Find the image entry in the zip by looking up the image name
                        ZipArchiveEntry? zippedImageEntry = zippedImages[meal.ImageName];
                        if (zippedImageEntry is null)
                        {
                            Utilities.DebugMsg($"Zip entry for {meal.ImageName} is missing");
                            continue; // we just checked this above, it really shouldn't be missing
                        }

                        // Extract the image to the image folder, possibly removing an existing one first
                        string fullFilename = Path.Combine(Meal.ImageFolderPath, zippedImageEntry.Name);

                        if (File.Exists(fullFilename) && !DeleteBeforeRestore)
                            Utilities.DebugMsg($"In {nameof(RestoreArchiveAsync)} file not restored {zippedImageEntry.Name} already exists");
                        else
                        {
                            zippedImageEntry.ExtractToFile(fullFilename, DeleteBeforeRestore);
                            localMealSummary.CheckImageFiles();
                            Utilities.DebugMsg($"In {nameof(RestoreArchiveAsync)}: zip archive entry {zippedImageEntry.Name} extracted to image folder for image {meal.ImageName}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ex.ReportCrash();
                    await Utilities.ShowAppSnackBarAsync("Failed to extract some images from archive");
                }
            }

            // Navigate to meal list after restore
            await App.GoToAsync(Routes.MealListByAgePage);

            // Clear selected archive after restore
            SelectedArchive = null;
            selectedArchiveZipPath = null;
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

    private bool CanRestoreArchive() => SelectedArchive is not null;

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
            SelectedArchive.SetDateRange(DateOnly.FromDateTime(StartDate), DateOnly.FromDateTime(FinishDate));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedArchive))]
    public partial DateTime FinishDate { get; set; } = DateTime.Now.Date;
    partial void OnFinishDateChanged(DateTime value)
    {
        if (FinishDate < StartDate)
            StartDate = FinishDate;
        if (SelectedArchive is not null)
            SelectedArchive.SetDateRange(DateOnly.FromDateTime(StartDate), DateOnly.FromDateTime(FinishDate));
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
            using var stream = new MemoryStream();
            int keysArchived = await CryptManager.ArchivePrivateKeysToZipAsync(KeyArchivePassword, stream);
            if (keysArchived == 0)
                await Utilities.ShowAppSnackBarAsync("No keys available to archive");
            else
            {
                stream.Position = 0; // Reset stream position before saving
                if (commandParameter is null || !commandParameter.Equals("share"))
                {
                    var result = await FileSaver.Default.SaveAsync(archiveName, stream);
                    if (result.IsSuccessful)
                        await Utilities.ShowAppSnackBarAsync($"{keysArchived} key(s) Archived to {result.FilePath}");
                    else
                        await Utilities.ShowAppSnackBarAsync("Failed to select file for key archive");
                }
                else
                {
                    // Save to a temporary file for sharing
                    string tempArchiveFilePath = Path.Combine(FileSystem.CacheDirectory, archiveName);
                    using (var fileStream = new FileStream(tempArchiveFilePath, FileMode.Create, FileAccess.Write))
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
        var result = await FilePicker.PickAsync(new PickOptions
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
            using var stream = await result.OpenReadAsync();
            (int restoredKeys, int totalKeys) = await CryptManager.RestorePrivateKeysFromZipAsync(KeyArchivePassword, stream);
            if (restoredKeys == totalKeys)
                await Utilities.ShowAppSnackBarAsync($"{restoredKeys} keys restored successfully.");
            else
                await Utilities.ShowAppSnackBarAsync($"{restoredKeys} of {totalKeys} keys restored successfully.");
        }
        catch (InvalidDataException ex)
        {
            string details = ex.Message;
            if (details is not null && details == "RSA fingerprint mismatch.")
                details = "Incorrect password.";
            await Utilities.ShowAppSnackBarAsync($"Key restore error: {details}");
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            await Utilities.ShowAppSnackBarAsync("Fault during key restore.");
        }
    }

    private bool CanArchiveOrRestoreKeys()
    {
        return !string.IsNullOrWhiteSpace(KeyArchivePassword);
    }

    [RelayCommand]
    private static async Task ClearCloudAsync ()
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
