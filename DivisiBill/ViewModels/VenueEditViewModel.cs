#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Models;
using DivisiBill.Services;

namespace DivisiBill.ViewModels;

[QueryProperty(nameof(ActiveVenue), "Venue")]
internal partial class VenueEditViewModel : ObservableObjectPlus
{
    [ObservableProperty]
    public partial Venue ActiveVenue { get; set; } = new();
    private readonly Action<Venue> AskCallerToShowMap;
    public VenueEditViewModel(Action<Venue> ShowMapParam)
    {
        AskCallerToShowMap = ShowMapParam;
        App.MyLocationChanged += App_MyLocationChanged;
    }

    public void Initialize()
    {
        Name = OriginalName = ActiveVenue.Name;
        Notes = ActiveVenue.Notes ?? string.Empty;
        MyLocation = ActiveVenue.IsLocationValid ? ActiveVenue.Location : null;
    }

    ~VenueEditViewModel()
    {
        App.MyLocationChanged -= App_MyLocationChanged;
    }
    private void App_MyLocationChanged(object? sender, EventArgs e) => Distance = App.GetDistanceTo(MyLocation);

    #region Properties

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewNameInvalid))]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OriginalName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Location? MyLocation { get; set; } = null;

    partial void OnMyLocationChanged(Location? value) => Distance = App.GetDistanceTo(value);

    [ObservableProperty]
    public partial int Distance { get; set; } = Distances.Unknown;

    public bool IsInUse => ActiveVenue.IsCurrentMeal;
    public bool HasUnsavedChanges => !(Utilities.StringFunctionallyEqual(Name, ActiveVenue.Name) && Utilities.StringFunctionallyEqual(Notes, ActiveVenue.Notes));
    public bool IsNewNameInvalid => string.IsNullOrWhiteSpace(Name) || Venue.AllVenues.Any((v) => ActiveVenue != v && Name.Equals(v.Name, StringComparison.Ordinal));
    #endregion
    public async Task SaveChanges()
    {
        // if the name being changed is the same as the one on the current meal, fix the meal too
        if (IsInUse)
            await Meal.CurrentMeal.ChangeVenueAsync(Name);
        // Change the stored name
        ActiveVenue.Name = Name;
        ActiveVenue.Notes = Notes;
        ActiveVenue.Location = MyLocation;
        // Make sure a changes are persisted
        await Venue.SaveSettingsAsync();
    }
    #region Commands
    [RelayCommand]
    private async Task Delete()
    {
        if (!IsInUse)
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
            await SaveChanges();
            await App.PopAsync();
        }
    }

    [RelayCommand]
    private void Restore() => Initialize();

    [RelayCommand]
    private void ClearLocation() => MyLocation = null;

    [RelayCommand]
    private void ShowMap() => AskCallerToShowMap(ActiveVenue);
    #endregion
}
