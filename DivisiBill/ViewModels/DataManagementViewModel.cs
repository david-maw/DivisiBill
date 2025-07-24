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

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly DateTime nextTime = DateTime.MinValue;

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
            await Utilities.ShowAppSnackBarAsync("Remote Access is not currently available");
            return;
        }
        if (Meal.RemoteMealList.Count == 0)
            await Utilities.ShowAppSnackBarAsync($"There are no remote bills");
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
                    await Utilities.ShowAppSnackBarAsync("Archive Sharing Complete");
                else
                    await Utilities.ShowAppSnackBarAsync("Archive Sharing Failed");
            }
            else if (ArchiveToDisk)
            {
                using Stream s = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read);
                FileSaverResult fileSaverResult = await FileSaver.Default.SaveAsync(Path.ChangeExtension(xmlFileName, ".zip"), s);
                if (fileSaverResult.IsSuccessful)
                    await Utilities.ShowAppSnackBarAsync("Archive to disk completed successfully");
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
    /// Command to restore an archive from a selected XML file, handling various types of data such as venues, people, and meals.
    /// </summary>
    /// <returns>Returns a task that completes when the restore operation is finished.</returns>
    [RelayCommand]
    public async Task RestoreArchiveAsync()
    {
        ZipArchive? zipArchive = null; // The zip archive containing the archive data, we are not loading XML directly
        Stream? archiveStream = null; // The stream containing archived data, either from a zip file or directly from an XML file.
        try
        {
            var result = await FilePicker.PickAsync(new PickOptions()
            {
                PickerTitle = "Please select an archive file",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                        { DevicePlatform.Android, new[] { "text/xml", "application/zip" } },
                        { DevicePlatform.WinUI, new[] { ".xml", "*.zip" } },
                }),
            });
            if (result is not null)
            {
                IsBusy = true;
                string archiveName = result.FileName;
                Utilities.DebugMsg($"In {nameof(RestoreArchiveAsync)}: file name {archiveName}");
                if (Path.GetExtension(archiveName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        zipArchive = ZipFile.OpenRead(result.FullPath);
                        Utilities.DebugMsg($"In {nameof(RestoreArchiveAsync)}: opened zip archive {archiveName}");
                    }
                    catch (Exception ex)
                    {
                        ex.ReportCrash();
                        await Utilities.ShowAppSnackBarAsync("Failed to open archive file");
                        return;
                    }
                    if (zipArchive is not null)
                    {
                        ZipArchiveEntry? zipArchiveEntry = zipArchive.Entries.FirstOrDefault();
                        if (zipArchiveEntry is not null)
                        {
                            archiveName = zipArchiveEntry.Name;
                            archiveStream = zipArchiveEntry.Open();
                        }
                        else
                            await Utilities.ShowAppSnackBarAsync("Archive file is empty");
                    }
                    else
                        await Utilities.ShowAppSnackBarAsync("Archive file is not a valid zip file");
                }
                else if (Path.GetExtension(archiveName).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                    archiveStream = await result.OpenReadAsync();
                else
                    await Utilities.ShowAppSnackBarAsync("Archive file must be a zip or xml file");
                // By this point we have an archive name and a stream to the archive, either from a zip file or directly from an XML file
                if (Path.GetExtension(archiveName).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    Archive? archive = null;
                    // For convenience we allow individual files to be deserialized 
                    if (archiveName.StartsWith("Venues"))
                    {
                        List<Venue> vl = Venue.DeserializeList(archiveStream);
                        if (vl is not null)
                            archive = new Archive() { Venues = vl };
                        else
                            Utilities.DebugMsg($"In SettingsViewModel.RestoreArchiveAsync, {archiveName} Venue.DeserializeList returned null");
                    }
                    else if (archiveName.StartsWith("People"))
                    {
                        List<Person> pl = Person.DeserializeList(archiveStream);
                        if (pl is not null)
                            archive = new Archive() { Persons = pl };
                        else
                            Utilities.DebugMsg($"In SettingsViewModel.RestoreArchiveAsync, {archiveName} Person.DeserializeList returned null");
                    }
                    else if (Utilities.TryDateTimeFromName(archiveName, out _)) // Serialized Meal name format
                    {
                        Meal m = Meal.LoadFromStream(archiveStream);
                        if (m is not null)
                            archive = new Archive() { Meals = new List<Meal>() { { m } } };
                        else
                            Utilities.DebugMsg($"In SettingsViewModel.RestoreArchiveAsync, {archiveName} Meal.LoadFromStream returned null");
                    }
                    else // Assume it is an archive
                        archive = Archive.FromStream(archiveStream);
                    if (archive is null)
                        await Utilities.ShowAppSnackBarAsync("Restore Failed, Archive was unusable");
                    else
                    {
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
                        // Now restore all the other items (which are not part of this ViewModel)
                        archive.DeleteBeforeRestore = DeleteBeforeRestore;
                        archive.OverwriteDuplicates = OverwriteDuplicates;
                        await archive.RestoreAsync(DateOnly.FromDateTime(StartDate), DateOnly.FromDateTime(FinishDate), OnlyRelated);
                        IsBusy = false;
                        await App.GoToAsync(Routes.MealListByAgePage);
                    }
                }
                else
                    Utilities.DebugMsg($"In SettingsViewModel.RestoreArchiveAsync, {archiveName} was not an xml or zip file");
            }
            else
                Utilities.DebugMsg($"In {nameof(RestoreArchiveAsync)}: returned file name was null");
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

    /// <summary>
    /// Get or set the earliest date in the range of bills which should be archived or restored
    /// </summary>
    [ObservableProperty]
    public partial DateTime StartDate { get; set; } = Archive.EarliestDateAllowed;

    [ObservableProperty]
    public partial DateTime FinishDate { get; set; } = DateTime.Now.Date;
}
