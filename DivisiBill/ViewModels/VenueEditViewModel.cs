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
    public bool HasUnsavedChanges => !(Utilities.StringFunctionallyEqual(Name, ActiveVenue.Name) && Utilities.StringFunctionallyEqual(Notes, ActiveVenue.Notes));
    public bool IsNewNameInvalid => string.IsNullOrWhiteSpace(Name) || Venue.AllVenues.Any((v) => ActiveVenue != v && Name.Equals(v.Name, StringComparison.Ordinal));
    #endregion
    public async Task SaveChanges()
    {
        // if the name being changed is the same as the one on the current meal, fix the meal too
        if (IsForCurrentMeal)
            await Meal.CurrentMeal.ChangeVenueAsync(Name);
        // Check to see if the current venue has changed
        if (ActiveVenue == Venue.Current)
            Venue.SetCurrentByName(null); // we have renamed the current venue, so there will not be a current venue until one is created
        else if (Venue.Current == null && Meal.CurrentMeal.VenueName == ActiveVenue.Name)
            Venue.SetCurrentByName(Meal.CurrentMeal.VenueName); // There wasn't previously a current venue but this is now it
        // Change the stored name
        ActiveVenue.Name = Name;
        ActiveVenue.Notes = Notes;
        ActiveVenue.Location = Location;
        // Make sure a changes are persisted
        await Venue.SaveSettingsAsync();
    }
    #region Commands
    [RelayCommand]
    private async Task Delete()
    {
        if (!IsForCurrentMeal)
        {
            ActiveVenue.Forget();
            var mealsForVenue = Meal.LocalMealList.Where((ms) => ms.IsLocal && ms.VenueName == ActiveVenue.Name);
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
    private async Task Save()
    {
        if (IsNewNameInvalid)
        {
            // Just restore the original name
            Name = ActiveVenue.Name;
        }
        else
        {
            bool nameChanged = !ActiveVenue.Name.Equals(Name, StringComparison.Ordinal);
            if (nameChanged)
            {
                // Before making name changes permanent, ensure that the user really wants to rename a Venue used with stored meals
                var count = Meal.LocalMealList.Count((ms) => ms.VenueName == ActiveVenue.Name && !ms.IsForCurrentMeal);
                if (count == 0 || await Utilities.AskAsync("Question",
                    $"There are {count} stored local bills for \"{ActiveVenue.Name}\", rename it anyway and disassociate them?"))
                {
                    await SaveChanges();
                    count = Meal.LocalMealList.Count((ms) => ms.VenueName == ActiveVenue.Name);
                    if (count > 0)
                        await Utilities.ShowAppSnackBarAsync($"{count} local stored bills use \"{ActiveVenue.Name}\"");
                    await App.PopAsync();
                }
            }
            else // name didn't change
            {
                await SaveChanges();
                await App.PopAsync();
            }
        }
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
        if (!Utilities.IsWinUI || App.BingMapsAllowed)
            await App.PushAsync(Routes.MapPage, "MapSettings", mapSettings);
    }
    #endregion
}
