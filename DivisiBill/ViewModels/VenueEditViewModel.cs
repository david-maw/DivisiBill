#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Models;
using DivisiBill.Services;
using System.Diagnostics.CodeAnalysis;

namespace DivisiBill.ViewModels;

internal partial class VenueEditViewModel : ObservableObjectPlus
{
    private readonly Venue originalVenue;
    private readonly Action ClosePage;
    private readonly Action<Venue> AskCallerToShowMap;
    public VenueEditViewModel(Venue venueParameter, Action ClosePageParam, Action<Venue> ShowMapParam)
    {
        originalVenue = venueParameter;
        ClosePage = ClosePageParam;
        AskCallerToShowMap = ShowMapParam;
        Initialize();
        App.MyLocationChanged += App_MyLocationChanged;
    }
    //TODO Report the conflict between MemberNotNill and ObserveableProperty
#pragma warning disable MVVMTK0034 // Direct field reference to [ObservableProperty] backing field
    [MemberNotNull(nameof(Name))]
    [MemberNotNull(nameof(Notes))]
#pragma warning restore MVVMTK0034 // Direct field reference to [ObservableProperty] backing field
    private void Initialize()
    {
        Name = originalVenue.Name ?? string.Empty;
        Notes = originalVenue.Notes ?? string.Empty;
        MyLocation = originalVenue.IsLocationValid ? originalVenue.Location : null;
    }

    ~VenueEditViewModel()
    {
        App.MyLocationChanged -= App_MyLocationChanged;
    }
    private void App_MyLocationChanged(object? sender, EventArgs e) => Distance = App.GetDistanceTo(MyLocation);

    #region Properties

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewNameInvalid))]
    public partial string Name { get; set; }

    public string OriginalName => originalVenue.Name;

    [ObservableProperty]
    public partial string Notes { get; set; }

    [ObservableProperty]
    public partial Location? MyLocation { get; set; } = null;

    partial void OnMyLocationChanged(Location? value) => Distance = App.GetDistanceTo(value);

    [ObservableProperty]
    public partial int Distance { get; set; } = Distances.Unknown;

    public bool IsInUse => originalVenue.IsCurrentMeal;
    public bool HasUnsavedChanges => !(Utilities.StringFunctionallyEqual(Name, originalVenue.Name) && Utilities.StringFunctionallyEqual(Notes, originalVenue.Notes));
    public bool IsNewNameInvalid => string.IsNullOrWhiteSpace(Name) || Venue.AllVenues.Any((v) => originalVenue != v && Name.Equals(v.Name, StringComparison.Ordinal));
    #endregion
    public async Task SaveChanges()
    {
        // if the name being changed is the same as the one on the current meal, fix the meal too
        if (IsInUse)
            await Meal.CurrentMeal.ChangeVenueAsync(Name);
        // Change the stored name
        originalVenue.Name = Name;
        originalVenue.Notes = Notes;
        originalVenue.Location = MyLocation;
        // Make sure a changes are persisted
        await Venue.SaveSettingsAsync();
    }
    #region Commands
    [RelayCommand]
    private async Task Delete()
    {
        if (!IsInUse)
        {
            originalVenue.Forget();
            var mealsForVenue = Meal.LocalMealList.Where((ms) => ms.IsLocal && ms.VenueName == originalVenue.Name);
            if (mealsForVenue.Any() && await Utilities.AskAsync("Question", "Do you want to delete local bills for " + originalVenue.Name))
            {
                foreach (MealSummary sum in mealsForVenue.OrderBy((ms) => ms.CreationTime))
                    await sum.DeleteAsync(doLocal: true, doRemote: false);
            }
            await Venue.SaveSettingsAsync();
            ClosePage?.Invoke();
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        if (IsNewNameInvalid)
        {
            // Just restore the original name
            Name = originalVenue.Name;
        }
        else
        {
            await SaveChanges();
            ClosePage?.Invoke();
        }
    }

    [RelayCommand]
    private void Restore() => Initialize();

    [RelayCommand]
    private void ClearLocation() => MyLocation = null;

    [RelayCommand]
    private void ShowMap() => AskCallerToShowMap(originalVenue);
    #endregion
}
