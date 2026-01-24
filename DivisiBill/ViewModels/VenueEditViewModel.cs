#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Models;
using DivisiBill.Services;

namespace DivisiBill.ViewModels;

[QueryProperty(nameof(ActiveVenue), "Venue")]
internal partial class VenueEditViewModel : ObservableObjectPlus
{
    private MapSettings? mapSettings;

    [ObservableProperty]
    public partial Venue ActiveVenue { get; set; } = new();
    public VenueEditViewModel() => App.MyLocationChanged += App_MyLocationChanged;

    public void Initialize()
    {
        Name = OriginalName = ActiveVenue.Name;
        Notes = ActiveVenue.Notes ?? string.Empty;
        Location = ActiveVenue.IsLocationValid ? ActiveVenue.Location : null;
        if (mapSettings is not null && mapSettings.VenueLocationHasChanged)
            Location = mapSettings.VenueLocation;
    }

    ~VenueEditViewModel()
    {
        App.MyLocationChanged -= App_MyLocationChanged;
    }
    private void App_MyLocationChanged(object? sender, EventArgs e) => Distance = App.GetDistanceTo(Location);

    #region Properties

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewNameInvalid))]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OriginalName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Location? Location { get; set; } = null;

    partial void OnLocationChanged(Location? value) => Distance = App.GetDistanceTo(value);

    [ObservableProperty]
    public partial int Distance { get; set; } = Distances.Unknown;

    public bool IsForCurrentMeal => ActiveVenue.IsForCurrentMeal;
    public bool NoChanges => Utilities.StringFunctionallyEqual(Name, ActiveVenue.Name)
        && Utilities.StringFunctionallyEqual(Notes, ActiveVenue.Notes)
        && Equals(Location, ActiveVenue.Location);
    public bool IsNewNameInvalid => string.IsNullOrWhiteSpace(Name) || Venue.AllVenues.Any((v) => ActiveVenue != v && Name.Equals(v.Name, StringComparison.Ordinal));
    #endregion
    /// <summary>
    /// Persists changes made to the active venue, including its name, notes, and location, and updates related meal and
    /// venue state as necessary.
    /// </summary>
    /// <remarks>If the name of the current venue is changed, this method updates the current meal and venue state to reflect
    /// the new name. Changes are saved asynchronously to ensure that all updates are persisted.</remarks>
    /// <returns>A task that represents the asynchronous save operation.</returns>
    public async Task SaveActive()
    {
        bool updateCurrentVenue = false;
        if (!Utilities.StringFunctionallyEqual(Name, ActiveVenue.Name))
        {
            // if the name being changed is the same as the one on an unfrozen current meal, fix the current meal too
            if (IsForCurrentMeal && !Meal.CurrentMeal.Frozen)
                await Meal.CurrentMeal.ChangeVenueAsync(Name);
            // Check to see if the current venue has changed
            if (ActiveVenue == Venue.Current)
                Venue.SetCurrentByName(null); // we have renamed the current venue, so there will not be a current venue until one is created
            else if (Venue.Current == null && Meal.CurrentMeal.VenueName == Name)
                updateCurrentVenue = true; // There wasn't previously a current venue but this is now it
            ActiveVenue.Name = Name; 
        }
        ActiveVenue.Notes = Notes;
        ActiveVenue.Location = Location;
        // Make sure any changes are persisted
        await Venue.SaveSettingsAsync();
        if (updateCurrentVenue) // Now that the list of venues has been updated we can try and select the new venue name we just stored
            Venue.SetCurrentByName(Name); 
    }
    #region Commands
    [RelayCommand]
    private async Task Delete()
    {
        if (!IsForCurrentMeal)
        {
            ActiveVenue.Forget();
            IEnumerable<MealSummary> mealsForVenue = Meal.LocalMealList.Where((ms) => ms.IsLocal && ms.VenueName == ActiveVenue.Name);
            if (mealsForVenue.Any() && await Utilities.AskAsync("Question", "Do you want to delete local bills for " + ActiveVenue.Name))
            {
                foreach (MealSummary sum in mealsForVenue.OrderBy((ms) => ms.CreationTime))
                    await sum.DeleteAsync(doLocal: true, doRemote: false);
            }
            await Venue.SaveSettingsAsync();
            await App.PopAsync();
        }
    }

    [RelayCommand]
    private async Task ExitPageAsync()
    {
        if (!NoChanges)
        {
            if (IsNewNameInvalid)
            {
                // Just restore the original name
                Name = ActiveVenue.Name;
                return; // Just go back to the page
            }
            else
            {
                bool nameChanged = !ActiveVenue.Name.Equals(Name, StringComparison.Ordinal);
                if (nameChanged)
                {
                    // Before making name changes permanent, ensure that the user really wants to rename a Venue used with stored meals
                    int count = Meal.LocalMealList.Count((ms) => ms.VenueName == ActiveVenue.Name && !(ms.IsForCurrentMeal && !Meal.CurrentMeal.Frozen));
                    if (count == 0 || await Utilities.AskAsync("Question",
                        $"There are {count} stored local bills for \"{ActiveVenue.Name}\", rename it anyway and disassociate them?"))
                    {
                        await SaveActive();
                        // Now check how many bills use the new venue name
                        count = Meal.LocalMealList.Count((ms) => ms.VenueName == Name);
                        if (count > 0)
                            await Utilities.ShowAppSnackBarAsync($"{count} local stored bills use \"{Name}\"");
                        else
                            await Utilities.ShowAppSnackBarAsync($"No local stored bills use \"{Name}\"");
                    }
                }
                else // name didn't change
                {
                    await SaveActive();
                }
            }
        }
        await App.PopAsync();
    }

    [RelayCommand]
    private void Restore() => Initialize();

    [RelayCommand]
    private void ClearLocation() => Location = null;

    [RelayCommand]
    private async Task ShowMap()
    {
        if (Utilities.IsWinUI)
        {
            await Utilities.ShowAppSnackBarAsync("Map is not available on Windows");
            return;
        }
        mapSettings = new(ActiveVenue.Name, ActiveVenue.Location);
        await App.PushAsync(Routes.MapPage, "MapSettings", mapSettings);
    }
    #endregion
}
