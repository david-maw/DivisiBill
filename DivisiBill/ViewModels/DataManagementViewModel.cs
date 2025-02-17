#nullable enable

using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Models;
using DivisiBill.Services;

namespace DivisiBill.ViewModels;

internal partial class DataManagementViewModel : ObservableObject
{

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly DateTime nextTime = DateTime.MinValue;

    [RelayCommand]
    private static async Task SelectOlder()
    {
        Models.Meal.SelectOlder();
        await App.GoToAsync(Routes.MealListByAgePage + "?IsSelectableList=true&count=true&ShowLocal=true&ShowRemote=false");
    }

    [RelayCommand]
    private async Task SelectDownloadable()
    {
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
                await App.GoToAsync(Routes.MealListByAgePage + "?command=SelectFirstUnallocatedLineItem");
            else
                await Utilities.ShowAppSnackBarAsync($"All {Meal.RemoteMealList.Count} remote bills are already downloaded");
        }
    }

    [RelayCommand]
    public async Task ArchiveAsync()
    {
        Archive archive = new(FilterByDate ? DateOnly.FromDateTime(StartDate) : DateOnly.MinValue, FilterByDate ? DateOnly.FromDateTime(FinishDate) : DateOnly.MaxValue, OnlyRelated);
        var filePath = Path.Combine(FileSystem.CacheDirectory, "DivisiBill" + archive.TimeName + ".xml");
        try
        {
            if (ArchiveShare)
            {
                Stream s = new FileStream(filePath, FileMode.OpenOrCreate);
                archive.ToJsonStream(s);
                await Share.RequestAsync(new ShareFileRequest
                {
                    Title = "Archive " + Path.GetFileName(filePath),
                    File = new ShareFile(filePath)
                });
                await Utilities.ShowAppSnackBarAsync("Archive Complete");
            }
            else if (ArchiveToDisk)
            {
                Stream s = new MemoryStream();
                archive.ToJsonStream(s);
                s.Position = 0;
                FileSaverResult fileSaverResult = new(null, null);
#if WINDOWS || ANDROID
                fileSaverResult = await FileSaver.Default.SaveAsync(Path.GetFileName(filePath), s);
#endif
                if (!fileSaverResult.IsSuccessful)
                    await Utilities.ShowAppSnackBarAsync("Archive Failed");
            }
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
        }
    }
    private static readonly PickOptions pickOptions
        = new()
        {
            PickerTitle = "Please select an archive file",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                    { DevicePlatform.Android, [ "text/xml" ] },
                    { DevicePlatform.WinUI, [ ".xml" ] },
            }),
        };

    [RelayCommand]
    public async Task RestoreArchiveAsync()
    {
        try
        {
            var result = await FilePicker.PickAsync(pickOptions);
            if (result is not null)
            {
                IsBusy = true;
                Utilities.DebugMsg($"In {nameof(RestoreArchiveAsync)}: file name {result.FileName}");
                if (Path.GetExtension(result.FileName).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    Archive? archive = null;
                    // For convenience we allow individual files to be deserialized 
                    if (result.FileName.StartsWith("Venues"))
                    {
                        using Stream stream = await result.OpenReadAsync();
                        List<Venue> vl = Venue.DeserializeList(stream);
                        if (vl is not null)
                            archive = new Archive() { Venues = vl };
                        else
                            Utilities.DebugMsg($"In SettingsViewModel.RestoreArchiveAsync, {result.FileName} Venue.DeserializeList returned null");
                    }
                    else if (result.FileName.StartsWith("People"))
                    {
                        using Stream stream = await result.OpenReadAsync();
                        List<Person> pl = Person.DeserializeList(stream);
                        if (pl is not null)
                            archive = new Archive() { Persons = pl };
                        else
                            Utilities.DebugMsg($"In SettingsViewModel.RestoreArchiveAsync, {result.FileName} Person.DeserializeList returned null");
                    }
                    else if (Utilities.TryDateTimeFromName(result.FileName, out _)) // Serialized Meal name format
                    {
                        using Stream stream = await result.OpenReadAsync();
                        Meal m = Meal.LoadFromStream(stream);
                        if (m is not null)
                            archive = new Archive() { Meals = new List<Meal>() { { m } } };
                        else
                            Utilities.DebugMsg($"In SettingsViewModel.RestoreArchiveAsync, {result.FileName} Meal.LoadFromStream returned null");
                    }
                    else // Assume it is an archive
                    {
                        var stream = await result.OpenReadAsync();
                        archive = Archive.FromStream(stream);
                    }
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

                            App.Settings.HadProSubscription = archive.UserSettings.HadProSubscription;

                            if (Utilities.IsDebug && archive.UserSettings.FakeLocation is not null && archive.UserSettings.FakeLocation.IsLocationValid)
                                await App.SetFakeLocation(archive.UserSettings.FakeLocation);
                        }
                        // Now restore all the other items (which are not part of this ViewModel)
                        archive.DeleteBeforeRestore = DeleteBeforeRestore;
                        archive.OverwriteDuplicates = OverwriteDuplicates;
                        await archive.RestoreAsync(FilterByDate ? DateOnly.FromDateTime(StartDate) : DateOnly.MinValue, FilterByDate ? DateOnly.FromDateTime(FinishDate) : DateOnly.MaxValue, OnlyRelated);
                        IsBusy = false;
                        await Utilities.ShowAppSnackBarAsync("Restore Complete");
                    }
                }
                else
                    Utilities.DebugMsg($"In SettingsViewModel.RestoreArchiveAsync, {result.FileName} did not end with .xml");
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
            IsBusy = false;
        }
    }

    [ObservableProperty]
    public partial bool ArchiveShare { get; set; } = true;

    [ObservableProperty]
    public partial bool ArchiveToDisk { get; set; } = false;

    [ObservableProperty]
    public partial bool FilterByDate { get; set; } = false;

    partial void OnFilterByDateChanged(bool value)
    {
        if (value)
            OnlyRelated = true;
    }

    [ObservableProperty]
    public partial bool OnlyRelated { get; set; } = false;

    [ObservableProperty]
    public partial bool DeleteBeforeRestore { get; set; } = false;

    [ObservableProperty]
    public partial bool OverwriteDuplicates { get; set; } = false;

    /// There's some strangeness below of DateOnly vs. DateTime, FinishDate and StartDate ought to be type DateOnly but that seems to behave as if it were XAML mode=OneWay as of Feb 2024  

    /// <summary>
    /// Get or set the earliest date in the range of bills which should be archived or restored
    /// </summary>
    public DateTime StartDate
    {
        get;
        set
        {
            SetProperty(ref field, value.Date);
            FilterByDate = true;
        }
    } = DateTime.Now.Date;

    public DateTime FinishDate
    {
        get;
        set
        {
            SetProperty(ref field, value.Date);
            FilterByDate = true;
        }
    } = DateTime.Now.Date;
}
