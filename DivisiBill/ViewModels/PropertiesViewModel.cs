using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Models;
using DivisiBill.Services;

namespace DivisiBill.ViewModels;

internal partial class PropertiesViewModel : ObservableObjectPlus
{
    #region Constructor/Destructor
    public PropertiesViewModel()
    {
        if (Meal.CurrentMeal is not null)
            Meal.CurrentMeal.PropertyChanged += CurrentMeal_PropertyChanged;
    }
    ~PropertiesViewModel()
    {
        if (Meal.CurrentMeal is not null)
            Meal.CurrentMeal.PropertyChanged -= CurrentMeal_PropertyChanged;
    }
    #endregion
    #region Enter / Exit Page
    public void LoadProperties()
    {
        LoadTaxString();
        LoadTaxRateString();
        LoadTipString();
        LoadTipRateString();
        LoadTipDeltaString();
        LoadScannedSubTotal();
        LoadScannedTax();
        LoadVenueNotes();
    }
    public void UnloadProperties()
    {
        UnloadTaxString();
        UnloadTaxRateString();
        UnloadTipString();
        UnloadTipRateString();
        UnloadTipDeltaString();
        UnloadScannedSubTotal();
        UnloadScannedTax();
        Meal.RequestSnapshot();
        UnloadVenueNotes();
    }
    #endregion
    #region Propagating Meal Events
    public void CurrentMeal_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
        if (e.PropertyName.Equals(nameof(Meal.VenueName)))
            LoadVenueNotes();
        else if (e.PropertyName.Equals(nameof(Meal.TipRate)))
            LoadTipRateString();
        else if (e.PropertyName.Equals(nameof(Meal.Tax)))
            LoadTaxString();
        else if (e.PropertyName.Equals(nameof(Meal.Tip)))
            LoadTipString();
        else if (e.PropertyName.Equals(nameof(Meal.TaxRate)))
            LoadTaxRateString();
        else if (e.PropertyName.Equals(nameof(Meal.CreationTime)))
        {
            OnPropertyChanged(nameof(DefaultFileName));
            OnPropertyChanged(nameof(ApproximateAge));
        }
        else if (e.PropertyName.Equals(nameof(Meal.LastChangeTime)))
        {
            OnPropertyChanged(nameof(IsLastChangeTimeDifferent));
            OnPropertyChanged(nameof(LastChangeTimeText));
        }
    }
    #endregion
    #region Totals, meal amounts and properties
    public decimal SubTotal => Meal.CurrentMeal.SubTotal;
    public string VenueName => Meal.CurrentMeal.VenueName;
    public Location AppLocation => App.MyLocation;
    public string ApproximateAge => Meal.CurrentMeal.ApproximateAge;
    public DateTime CreationTime => Meal.CurrentMeal.CreationTime;
    public DateTime LastChangeTime => Meal.CurrentMeal.LastChangeTime;
    public string LastChangeTimeText => Meal.CurrentMeal.Summary.GetLastChangeString();
    public bool IsLastChangeTimeDifferent => !Utilities.WithinOneSecond(CreationTime, LastChangeTime);
    public string DiagnosticInfo => Meal.CurrentMeal?.DiagnosticInfo ?? string.Empty;
    public string DefaultFileName => IsDefault ? null : Meal.CurrentMeal.FileName;
    #endregion
    #region Tip
    #region TipRateString
    private void LoadTipRateString() => TipRateString = string.Format("{0}", TipRate);

    [RelayCommand]
    private void UnloadTipRateString()
    {
        if (TipRateStringIsValid)
            TipRate = int.Parse(TipRateString);
    }
    [ObservableProperty]
    public partial string TipRateString { get; set; }
    public bool TipRateStringIsValid { get; set; }
    #endregion
    #region TipString
    private void LoadTipString() => TipString = string.Format("{0:0.00}", Tip);
    [RelayCommand]
    private void UnloadTipString()
    {
        if (TipStringIsValid)
        {
            Tip = Decimal.Parse(TipString);
            LoadTipString();
        }
    }

    [ObservableProperty]
    public partial string TipString { get; set; }
    public bool TipStringIsValid { get; set; }
    public int TipRate
    {
        get => Convert.ToInt32(Meal.CurrentMeal.TipRate * 100);
        set
        {
            Meal.CurrentMeal.TipRate = value / 100.0;
            OnPropertyChanged(nameof(IsDefaultTipRate));
        }
    }
    public decimal Tip
    {
        get => Meal.CurrentMeal.Tip;
        set
        {
            Meal.CurrentMeal.SetRateFromTip(value);
            LoadTipRateString();
            LoadTipDeltaString();
        }
    }
    public decimal TipDelta
    {
        get => Meal.CurrentMeal.TipDelta;
        set
        {
            if (Meal.CurrentMeal.TipDelta != value)
            {
                Meal.CurrentMeal.TipDelta = value;
                LoadTipDeltaString();
                OnPropertyChanged(nameof(TipDeltaString));
            }
        }
    }
    #region TipDeltaString
    private void LoadTipDeltaString() => 
        TipDeltaString = TipDelta==0 ? "" : string.Format("{0:0.00}", TipDelta);

    [RelayCommand]
    private void UnloadTipDeltaString()
    {
        if (TipDeltaStringIsValid)
        {
            TipDelta = string.IsNullOrWhiteSpace(TipDeltaString) ? 0 :decimal.Parse(TipDeltaString);
            LoadTipDeltaString();
        }
    }

    [ObservableProperty]
    public partial string TipDeltaString { get; set; }
    public bool TipDeltaStringIsValid { get; set; }
    #endregion
    #endregion
    #endregion
    #region Tax
    #region TaxRateString
    private void LoadTaxRateString() => TaxRateString = string.Format("{0:0.00}", TaxRate);

    [RelayCommand]
    private void UnloadTaxRateString()
    {
        if (TaxRateStringIsValid)
        {
            TaxRate = double.Parse(TaxRateString);
            LoadTaxRateString();
        }
    }

    [ObservableProperty]
    public partial string TaxRateString { get; set; }
    public bool TaxRateStringIsValid { get; set; }
    #endregion
    #region TaxString
    private void LoadTaxString() => TaxString = string.Format("{0:0.00}", Meal.CurrentMeal.Tax);
    [RelayCommand]
    private void UnloadTaxString()
    {
        if (TaxStringIsValid)
        {
            Tax = decimal.Parse(TaxString);
            LoadTaxString();
        }
    }
    [ObservableProperty]
    public partial string TaxString { get; set; }
    public bool TaxStringIsValid { get; set; }
    #endregion
    #region TaxDeltaString
    // This is a readonly field, so it is much simpler
    public string TaxDeltaString => (Meal.CurrentMeal.TaxDelta == 0) ? "0" : string.Format("{0:0.00}", Meal.CurrentMeal.TaxDelta);
    #endregion
    #region Scanned Tax
    [ObservableProperty]
    public partial string ScannedTaxString { get; set; }

    [ObservableProperty]
    public partial bool ScannedTaxStringIsValid { get; set; }

    private void LoadScannedTax()
    {
        ScannedTaxString = (ScannedTax == 0) ? "" : string.Format("{0:0.00}", ScannedTax);
    }

    [RelayCommand]
    private void UnloadScannedTax()
    {
        if (ScannedTaxStringIsValid)
        {
            ScannedTax = string.IsNullOrEmpty(ScannedTaxString) ? 0 : decimal.Parse(ScannedTaxString);
            LoadScannedTax();
        }
    }
    #endregion
    #endregion
    #region Scanned Subtotal (capitalized as SubTotal)
    [ObservableProperty]
    public partial string ScannedSubTotalString { get; set; }

    private void LoadScannedSubTotal() => ScannedSubTotalString = (ScannedSubTotal == 0) ? "" : string.Format("{0:0.00}", ScannedSubTotal);

    [RelayCommand]
    private void UnloadScannedSubTotal()
    {
        if (ScannedSubTotalStringIsValid)
        {
            ScannedSubTotal = string.IsNullOrEmpty(ScannedSubTotalString) ? 0 : decimal.Parse(ScannedSubTotalString);
            LoadScannedSubTotal();
        }
    }

    [ObservableProperty]
    public partial bool ScannedSubTotalStringIsValid { get; set; }
    #endregion
    #region Meal Data
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
        set
        {
            if (Meal.CurrentMeal.TaxDelta != value)
            {
                Meal.CurrentMeal.TaxDelta = value;
                OnPropertyChanged(nameof(TaxDeltaString));
            }
        }
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
        if (Meal.CurrentMeal is null) // rare - only seems to happen in Play Store testing
            return;
        if (Meal.CurrentMeal.IsDefault)
            await Utilities.DisplayAlertAsync("Default Bill", "You cannot save the default bill. Modify it and try again.", "ok");
        else
        {
            await Meal.CurrentMeal.SaveSnapshotAsync();
            await App.GoToAsync(Routes.MealListByAgePage);
        }
    }
    #endregion
    #region Venue Notes
    [ObservableProperty]
    public partial string VenueNotes { get; set; }

    partial void OnVenueNotesChanged(string value)
    {
        venueNotesChanged = true;
    }
    private void LoadVenueNotes()
    {
        currentVenue = Venue.FindVenueByName(VenueName);
        VenueNotes = currentVenue?.Notes;
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
    private Venue currentVenue;
    #endregion
    #region Handling Defaults
    public bool IsDefault => Meal.CurrentMeal.IsDefault;
    public bool IsDefaultTipRate => App.Settings.DefaultTipRate == TipRate;
    public bool IsDefaultTaxRate => App.Settings.DefaultTaxRate == Meal.CurrentMeal.TaxRate;
    public bool IsDefaultTipOnTax => App.Settings.DefaultTipOnTax == TipOnTax;
    public bool IsDefaultCouponAfterTax => App.Settings.DefaultTaxOnCoupon == CouponAfterTax;
    public bool IsDefaultTip => IsDefaultTipOnTax && IsDefaultTipRate;
    public bool IsDefaultTax => IsDefaultCouponAfterTax && IsDefaultTaxRate;
    public int DefaultTipRate => App.Settings.DefaultTipRate;
    public double DefaultTaxRate => App.Settings.DefaultTaxRate;
    public bool DefaultTipOnTax => App.Settings.DefaultTipOnTax;
    #endregion
}
