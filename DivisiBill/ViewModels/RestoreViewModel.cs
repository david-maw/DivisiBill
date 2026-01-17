#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Services;

namespace DivisiBill.ViewModels;

/// <summary>
/// Represents the view model for restoring archived bills, providing properties and commands to manage the restore
/// process, select archives, and configure restore options. Used only for Android intent launch scenarios, for the
/// regular restore workflow the <see cref="DataManagementViewModel"/> is used.
/// </summary>
/// <remarks>This view model is intended for use in scenarios where users need to restore data from an archive,
/// such as when the application is launched via a file intent. It exposes properties to control the restore workflow,
/// including date range selection, archive selection, and restore options like deleting existing data or overwriting
/// duplicates. The view model also provides commands to initiate the restore operation and handle navigation after
/// completion. Thread safety is not guaranteed; members should be accessed from the UI thread.</remarks>
public partial class RestoreViewModel : ObservableObject
{
    public async Task WaitForUpdatesAsync()
    {
        StreamRequest intentInfo = await App.Current.IntentQueue.DequeueAsync(CancellationToken.None);
        (Archive archive, string errorMsg) = await Archive.DeserializeAnyAsync(intentInfo.FileStream, intentInfo.MimeType);
        if (archive is null)
            IntentDescription = $"DivisiBill could not open the archive: " + errorMsg;
        else
        {
            IntentDescription = $"DivisiBill opened an archive containing {archive.AllMeals.Count} bills";
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
    }
    // Text describing how the app was launched and intent details
    [ObservableProperty]
    public partial string? IntentDescription { get; set; } = "DivisiBill launched by a file intent";
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial Archive? SelectedArchive { get; set; } = null;

    /// <summary>
    /// Command to restore the previously selected archive (SelectedArchive). This restores archive items and selectively
    /// extracts images from the original zip only for the meals that were restored.
    /// </summary>
    [RelayCommand]
    public async Task RestoreArchiveAsync()
    {
        IsBusy = true;
        try
        {
            if (SelectedArchive is null)
                return;

            // We never apply user settings from the archive - use the App Data Management Page Restore option for that.

            // Restore the data items
            (bool restoreWorked, string restoreFailureText) = await SelectedArchive.RestoreAnyAsync(DeleteBeforeRestore, OverwriteDuplicates, OnlyRelated);

            if (restoreWorked)
            {
                IntentDescription = $"Restored {SelectedMealsCount} bills";
                await Task.Delay(1000); // Give user a moment to see the success message
                ExitAction();
            }
            else
            {
                SelectedArchive.ClearDateRange(); // disable restore
                IntentDescription = restoreFailureText != null ? $"Restore had a problem: {restoreFailureText}" : "Restore failed";
            }
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            IntentDescription = "Restore Faulted, Archive was unusable";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [ObservableProperty]
    public partial int SelectedMealsCount { get; private set; } = 0;

    [RelayCommand]
    public void ExitAction() => ExitPage?.Invoke();

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
    [NotifyPropertyChangedFor(nameof(SelectedArchive))] // So as to update SelectedArchive.SelectedMeals.Count when date changes
    public partial DateTime StartDate { get; set; } = Archive.EarliestDateAllowed;

    partial void OnStartDateChanged(DateTime value)
    {
        if (StartDate > FinishDate)
            FinishDate = StartDate;
        if (SelectedArchive is not null)
            SelectedMealsCount = SelectedArchive.SetDateRange(DateOnly.FromDateTime(StartDate), DateOnly.FromDateTime(FinishDate));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedArchive))] // So as to update SelectedArchive.SelectedMeals.Count when date changes
    public partial DateTime FinishDate { get; set; } = DateTime.Now.Date;
    partial void OnFinishDateChanged(DateTime value)
    {
        if (FinishDate < StartDate)
            StartDate = FinishDate;
        if (SelectedArchive is not null)
            SelectedMealsCount = SelectedArchive.SetDateRange(DateOnly.FromDateTime(StartDate), DateOnly.FromDateTime(FinishDate));
    }

    public Action? ExitPage = null;
}
