using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Models;
using DivisiBill.Services;

namespace DivisiBill.ViewModels;

internal partial class PropertiesViewModel : ObservableObjectPlus
{
    #region Constants
    public static int StoppedTypingTimeThreshold = 2000;
    #endregion
    #region Constructor/Destructor
    public PropertiesViewModel()
    {
        Meal.CurrentMeal.PropertyChanged += CurrentMeal_PropertyChanged;
        Meal.CurrentMealChanged += (oldValue, newValue) =>
        {
            oldValue.PropertyChanged -= CurrentMeal_PropertyChanged;
            newValue.PropertyChanged += CurrentMeal_PropertyChanged;
            OnPropertyChanged(string.Empty); // just refresh everything, it's easier than trying to figure out what changed
        };
    }

    ~PropertiesViewModel()
    {
        Meal.CurrentMeal.PropertyChanged -= CurrentMeal_PropertyChanged;
    }
    #endregion
    #region Enter / Exit Page
    public void LoadProperties()
    {
        RefreshDefaultProperties(); // Because there's no notification if they change while the page isn't open
        LoadVenueNotes();
    }
    public void UnloadProperties()
    {
        Meal.RequestSnapshot();
        UnloadVenueNotes();
    }
    #endregion
    #region Propagating Meal Events
    public void CurrentMeal_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null)
            return; // Don't know what changed, so just ignore it.
        OnPropertyChanged(e.PropertyName);
        if (e.PropertyName.Equals(nameof(Meal.VenueName)))
            LoadVenueNotes();
        else if (e.PropertyName.Equals(nameof(Meal.CreationTime)))
            OnPropertyChanged(nameof(DefaultFileName));
        else if (e.PropertyName.Equals(nameof(Meal.LastChangeTime)))
        {
            OnPropertyChanged(nameof(IsLastChangeTimeDifferent));
            OnPropertyChanged(nameof(LastChangeTimeText));
        }
        else if (e.PropertyName.Equals(nameof(Meal.Tax)))
            OnPropertyChanged(nameof(ScannedTax)); // Because they may no longer match
        else if (e.PropertyName.Equals(nameof(Meal.SubTotal)))
            OnPropertyChanged(nameof(ScannedSubTotal)); // Because they may no longer match
        else if (e.PropertyName.Equals(nameof(Meal.ScannedTax)))
            OnPropertyChanged(nameof(Tax)); // Because they may no longer match
        else if (e.PropertyName.Equals(nameof(Meal.ScannedSubTotal)))
            OnPropertyChanged(nameof(SubTotal)); // Because they may no longer match
    }
    #endregion
    #region Totals, meal amounts and properties
    public decimal SubTotal => Meal.CurrentMeal.SubTotal;
    public string VenueName => Meal.CurrentMeal.VenueName;
    public Location? AppLocation => App.MyLocation;
    public DateTime CreationTime => Meal.CurrentMeal.CreationTime;
    public DateTime LastChangeTime => Meal.CurrentMeal.LastChangeTime;
    public string? LastChangeTimeText => Meal.CurrentMeal.Summary.GetLastChangeString();
    public bool IsLastChangeTimeDifferent => !Utilities.WithinOneSecond(CreationTime, LastChangeTime);
    public string DiagnosticInfo => Meal.CurrentMeal?.DiagnosticInfo ?? string.Empty;
    public string DefaultFileName => IsDefault ? "" : Meal.CurrentMeal.FileName;
    #endregion
    #region Tip
    public int TipRate
    {
        get => Convert.ToInt32(Meal.CurrentMeal.TipRate * 100);
        set => Meal.CurrentMeal.TipRate = value / 100.0;
    }
    public decimal Tip
    {
        get => Meal.CurrentMeal.Tip;
        set => Meal.CurrentMeal.SetRateFromTip(value);
    }
    public decimal TipDelta
    {
        get => Meal.CurrentMeal.TipDelta;
        set => Meal.CurrentMeal.TipDelta = value;
    }
    #endregion
    #region Tax
    /// <summary>
    /// The current meal tax rate as a percentage
    /// </summary>
    public double TaxRate
    {
        get => Meal.CurrentMeal.TaxRate * 100;
        set
        {
            Meal.CurrentMeal.TaxRate = value / 100;
            OnPropertyChanged(nameof(IsDefaultTaxRate));
        }
    }
    public decimal Tax
    {
        get => Meal.CurrentMeal.Tax;
        set => Meal.CurrentMeal.SetRateFromTax(value);
    }
    public bool TipOnTax
    {
        get => Meal.CurrentMeal.TipOnTax;
        set
        {
            Meal.CurrentMeal.TipOnTax = value;
            OnPropertyChanged(nameof(IsDefaultTipOnTax));
        }
    }
    public decimal TaxDelta
    {
        get => Meal.CurrentMeal.TaxDelta;
        set => Meal.CurrentMeal.TaxDelta = value;
    }
    public bool CouponAfterTax
    {
        get => Meal.CurrentMeal.IsCouponAfterTax;
        set
        {
            Meal.CurrentMeal.IsCouponAfterTax = value;
            OnPropertyChanged(nameof(IsDefaultCouponAfterTax));
        }
    }
    #endregion
    #region Scans and Encryption
    public decimal ScannedSubTotal
    {
        get => Meal.CurrentMeal.ScannedSubTotal;
        set => Meal.CurrentMeal.ScannedSubTotal = value;
    }
    public decimal ScannedTax
    {
        get => Meal.CurrentMeal.ScannedTax;
        set => Meal.CurrentMeal.ScannedTax = value;
    }
    public bool IsEncrypted => Meal.CurrentMeal.Summary.IsEncrypted;
    #endregion    
    #region Meal Manipulation
    [RelayCommand]
    private async Task MarkCurrentMealAsNew()
    {
        if (Meal.CurrentMeal.IsDefault)
            await Utilities.DisplayAlertAsync("Default Bill", "Marking the default meal as new does nothing.", "ok");
        else
            await Meal.CurrentMeal.MarkAsNewAsync("User", unconditional: true);
    }
    [RelayCommand]
    private async Task SaveCurrentMeal()
    {
        if (Meal.CurrentMeal.IsDefault)
            await Utilities.DisplayAlertAsync("Default Bill", "You cannot save the default bill. Modify it and try again.", "ok");
        else
        {
            await Meal.CurrentMeal.SaveSnapshotAsync();
            await App.GoToAsync(Routes.MealListByAgePage);
        }
    }
    [RelayCommand]
    private async Task OpenVenue()
    {
        if (Venue.FindVenueByName(VenueName) is Venue venue)
            await App.PushAsync(Routes.VenueListByNamePage);
        else if (await Utilities.AskAsync("Question", $"Venue \"{VenueName}\" not found, do you want to create it?"))
            await App.PushAsync(Routes.VenueEditPage, "Venue", Venue.SelectOrAddVenue(VenueName, $"Created from bill {Meal.CurrentMeal.Summary.Id} on {DateTime.Now:d}"));
        else
            await App.PushAsync(Routes.VenueListByNamePage);
    }
    #endregion
    #region Venue Notes
    [ObservableProperty]
    public partial string VenueNotes { get; set; } = string.Empty;

    partial void OnVenueNotesChanged(string value)
    {
        venueNotesChanged = true;
    }
    private void LoadVenueNotes()
    {
        currentVenue = Venue.FindVenueByName(VenueName);
        VenueNotes = currentVenue?.Notes ?? string.Empty;
        venueNotesChanged = false;
    }
    private void UnloadVenueNotes()
    {
        if (venueNotesChanged && currentVenue is not null)
        {
            currentVenue.Notes = VenueNotes;
            _ = Venue.SaveSettingsAsync();
        }
    }
    /// <summary>
    /// Set true if the notes have been changed since being loaded
    /// </summary>
    private bool venueNotesChanged;
    private Venue? currentVenue;
    #endregion
    #region Handling Defaults
    public bool IsDefault => Meal.CurrentMeal.IsDefault;
    public bool IsDefaultTaxRate => App.Settings.DefaultTaxRate == Meal.CurrentMeal.TaxRate;
    public bool IsDefaultTipOnTax => App.Settings.DefaultTipOnTax == TipOnTax;
    public bool IsDefaultCouponAfterTax => App.Settings.DefaultTaxOnCoupon == CouponAfterTax;
    public int DefaultTipRate => App.Settings.DefaultTipRate;
    public double DefaultTaxRatePercentage => App.Settings.DefaultTaxRate * 100;
    private void RefreshDefaultProperties()
    {
        OnPropertyChanged(nameof(IsDefault));
        OnPropertyChanged(nameof(IsDefaultTaxRate));
        OnPropertyChanged(nameof(IsDefaultTipOnTax));
        OnPropertyChanged(nameof(IsDefaultCouponAfterTax));
        OnPropertyChanged(nameof(IsDefaultTaxRate));
        OnPropertyChanged(nameof(DefaultTipRate));
        OnPropertyChanged(nameof(DefaultTaxRatePercentage));
    }
    #endregion
}
