// Ignore Spelling: Fi

using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Services;

namespace DivisiBill.ViewModels;

public partial class SettingsViewModel : ObservableObjectPlus
{
    #region Initialization
    private readonly Application currentApp;
    private static MealViewModel? Mvm => App.Current?.Resources is { } r && r.TryGetValue("MealViewModel", out object? obj) ? obj as MealViewModel : null;
    public SettingsViewModel()
    {
        if (!App.LicenseChecked)
            App.ProEditionVerified += App_ProEditionVerified;
        FakeLocationMapSettings = new MapSettings("Fake Location", FakeLocation ?? App.MyLocation);
        currentApp = Application.Current is null ? throw new NullReferenceException() : Application.Current;
        ScanOption = 2;
        App.MyLocationChanged += App_MyLocationChanged;
        Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;
    }
    ~SettingsViewModel()
    {
        App.ProEditionVerified -= App_ProEditionVerified;
        App.MyLocationChanged -= App_MyLocationChanged;
        Connectivity.ConnectivityChanged -= Connectivity_ConnectivityChanged;
    }
    private void Connectivity_ConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        OnPropertyChanged(nameof(WiFiStatus));
        OnPropertyChanged(nameof(InternetEnabled));
        OnPropertyChanged(nameof(InternetEnabledAndLicensed));
    }
    private void App_MyLocationChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(AppLocation));
    private void App_ProEditionVerified(object? sender, EventArgs e) => RefreshValues();
    public void RefreshValues()
    {
        // These are the values which are held externally and might change while we're on another page
        // This first set are just readonly views of App or billing values 
        OnPropertyChanged(nameof(IsCloudAccessAllowed));
        OnPropertyChanged(nameof(InternetEnabledAndLicensed));
        OnPropertyChanged(nameof(LicenseChecked));
        OnPropertyChanged(nameof(HasProSubscription));
        OnPropertyChanged(nameof(InvalidProSubscription));
        OnPropertyChanged(nameof(ProSubscriptionId));
        OnPropertyChanged(nameof(ScansLeft));
        OnPropertyChanged(nameof(IsOcrPurchaseAllowed));
        OnPropertyChanged(nameof(HasOcrLicense));
        OnPropertyChanged(nameof(InvalidOcrLicense));
        OnPropertyChanged(nameof(OcrLicenseId));
        // These are fake location related and they should change whenever it does 
        FakeLocation = App.FakeLocation;
        UseFakeLocation = App.UseFakeLocation;
        HasPassword = CryptManager.HasStoredPassword;
        // Ones where side effects are needed when they change
        IsLimited = App.IsLimited;
        // Meal default values may have changed if the current meal changed
        if (Mvm is not null)
        {
            DefaultTipOnTax = Mvm.DefaultTipOnTax;
            DefaultTipRatePercentage = Mvm.DefaultTipRatePercentage;
            DefaultTaxOnCoupon = Mvm.DefaultTaxOnCoupon;
            DefaultTaxRatePercentage = Mvm.DefaultTaxRatePercentage;
        }
    }
    public void OnNavigatedTo()
    {
        RefreshValues();
        if (FakeLocationMapSettings.VenueLocationHasChanged)
        {
            FakeLocationMapSettings.VenueLocationHasChanged = false; // So we do not reuse it accidentally
            if (FakeLocationMapSettings.VenueLocation is null || FakeLocationMapSettings.VenueLocation.IsAccurate())
            {
                if (FakeLocationMapSettings.VenueLocation is null)
                    UseFakeLocation = false;
                FakeLocation = FakeLocationMapSettings.VenueLocation;
            }
        }
    }
    #endregion
    #region Commands
    [RelayCommand]
    private async Task ChangePassword() => _ = await Shell.Current.ShowPopupAsync<bool>(new Controls.ChangePasswordPopup());
    [RelayCommand]
    private void ClearPassword()
    {
        CryptManager.ClearPassword();
        HasPassword = false;
    }

    [RelayCommand]
    private async Task OpenWebAsync() => await Launcher.OpenAsync(new Uri("https://learn.microsoft.com/en-us/dotnet/maui/what-is-maui"));

    [RelayCommand]
    private async Task OpenAutoPlusAsync() => await Launcher.OpenAsync(new Uri("http://www.autopl.us"));

    [RelayCommand]
    private async Task LicensingHelp() => await App.PushAsync($"{Routes.HelpPage}?page=licensing");

    [RelayCommand]
    private async Task ChangeLicensesAsync()
    {
        await App.PushAsync(Routes.LicensesPage);
    }

    [RelayCommand]
    private async Task SubscriptionHelpAsync()
    {
        Shell.Current.FlyoutIsPresented = false;
        await App.PushAsync($"{Routes.HelpPage}?page=licensing");
    }

    [RelayCommand]
    private void SystemSettings() => AppInfo.Current.ShowSettingsUI();

    [RelayCommand]
    private void EnableHints() => App.Settings.EnableHints();

    [RelayCommand]
    private void ResetCheckBoxes() => App.Settings.ResetCheckboxes();
    #endregion
    #region Transient Properties
    [ObservableProperty]
    public partial bool IsLimited { get; set; } = true; // Be sure to react to it being set false
    partial void OnIsLimitedChanged(bool value)
    {
        App.IsLimited = value;
    }

    public bool IsOcrPurchaseAllowed => ScansLeft < Billing.ScansWarningLevel; // Includes the case where the user has purchased no scans yet
    public int ScanOption
    {
        get => App.ScanOption;
        set
        {
            if (App.ScanOption != value)
            {
                App.ScanOption = value;
                OnPropertyChanged();
            }
        }
    }
    #endregion
    #region Persistent Properties
    [ObservableProperty]
    public partial bool SendCrashYes { get; set; } = App.Settings.SendCrashYes;
    partial void OnSendCrashYesChanged(bool value) => App.Settings.SendCrashYes = value;

    [ObservableProperty]
    public partial bool SendCrashAsk { get; set; } = App.Settings.SendCrashAsk;
    partial void OnSendCrashAskChanged(bool value) => App.Settings.SendCrashAsk = value;

    [ObservableProperty]
    public partial bool IsCloudAccessAllowed { get; set; } = App.Settings.IsCloudAccessAllowed;
    partial void OnIsCloudAccessAllowedChanged(bool value)
    {
        App.Settings.IsCloudAccessAllowed = value;
        if (!value)
        {
            WiFiOnly = true;
            BackupImages = false;
        }
    }

    [ObservableProperty]
    public partial bool StartFresh { get; set; } = App.Settings.StartFresh;
    partial void OnStartFreshChanged(bool value) => App.Settings.StartFresh = value;

    [ObservableProperty]
    public partial bool WiFiOnly { get; set; } = App.Settings.WiFiOnly;
    partial void OnWiFiOnlyChanged(bool value)
    {
        App.Settings.WiFiOnly = value;
        if (value)
            BackupImagesOnlyWiFi = true;
    }

    [ObservableProperty]
    public partial bool BackupImages { get; set; } = App.Settings.BackupImages;
    partial void OnBackupImagesChanged(bool value)
    {
        App.Settings.BackupImages = value;
        if (!value)
            BackupImagesOnlyWiFi = true;
    }

    [ObservableProperty]
    public partial bool BackupImagesOnlyWiFi { get; set; } = App.Settings.BackupImagesOnlyWiFi;
    partial void OnBackupImagesOnlyWiFiChanged(bool value)
    {
        App.Settings.BackupImagesOnlyWiFi = value;
        if (!value)
            WiFiOnly = false;
    }

    #region Application Defaults via MealViewModel
    // These are convenience properties - they are really App properties but there's no change notification mechanism for them
    // so we set them in the current meal viewmodel so that changes here are reflected there (though that is not a common occurrence)
    [ObservableProperty]
    public partial bool DefaultTipOnTax { get; set; } = Mvm?.DefaultTipOnTax ?? App.Settings.DefaultTipOnTax;
    partial void OnDefaultTipOnTaxChanged(bool value) { Mvm?.DefaultTipOnTax = value; }

    [ObservableProperty]
    public partial decimal DefaultTipRatePercentage { get; set; } = Mvm?.DefaultTipRatePercentage ?? (decimal)App.Settings.DefaultTipRate;
    partial void OnDefaultTipRatePercentageChanged(decimal value) { Mvm?.DefaultTipRatePercentage = value; }

    [ObservableProperty]
    public partial bool DefaultTaxOnCoupon { get; set; } = Mvm?.DefaultTaxOnCoupon ?? App.Settings.DefaultTaxOnCoupon;
    partial void OnDefaultTaxOnCouponChanged(bool value) { Mvm?.DefaultTaxOnCoupon = value; }

    [ObservableProperty]
    public partial decimal DefaultTaxRatePercentage { get; set; } = Mvm?.DefaultTaxRatePercentage ?? (decimal)(App.Settings.DefaultTaxRate * 100);
    partial void OnDefaultTaxRatePercentageChanged(decimal value) { Mvm?.DefaultTaxRatePercentage = value; }
    #endregion
    #endregion
    #region Cryptography Properties
    [ObservableProperty]
    public partial bool HasPassword { get; set; }
    #endregion
    #region Cloud Access Properties
    /// <summary>
    /// Whether or not Internet access exists
    /// </summary>
    public bool InternetEnabled => Connectivity.NetworkAccess == NetworkAccess.Internet;

    /// <summary>
    /// Whether or not Internet access exists and we are running the Professional edition
    /// Note that cloud archiving may still not be allowed by the user
    /// </summary>
    public bool InternetEnabledAndLicensed => InternetEnabled && !App.IsLimited;
    public string WiFiStatus
    {
        get
        {
            IEnumerable<ConnectionProfile> profiles = Connectivity.ConnectionProfiles;
            if (profiles.Contains(ConnectionProfile.WiFi))
            {
                return "WiFi enabled";// Active Wi-Fi connection.
            }
            else
                return "No WiFi detected";
        }
    }
    #endregion
    public bool WsUriDefined => App.WsUriDefined;
    public bool LicenseChecked => App.LicenseChecked;
    public bool HasProSubscription => Billing.ProPurchase is not null;
    public bool InvalidProSubscription => Billing.ProPurchase is not null && Billing.ProPurchase.State != InAppBilling.PurchaseState.Purchased;
    public string? ProSubscriptionId => Billing.ProPurchase?.Id;
    public int ScansLeft => Billing.ScansLeft;
    public bool HasOcrLicense => Billing.OcrPurchase is not null;
    public bool InvalidOcrLicense => Billing.OcrPurchase is not null && Billing.OcrPurchase.State != InAppBilling.PurchaseState.Purchased;
    public string? OcrLicenseId => Billing.OcrPurchase?.Id;
    public string BaseAddress => App.WsUriDefined ? CallWs.BaseAddress?.ToString() ?? "" : "";
    public string LastUse => App.Settings.LastUse.ToString();
    public bool Dark
    {
        set
        {
            if (value != Dark)
            {
                currentApp.UserAppTheme = value ? AppTheme.Dark : AppTheme.Light;
                App.SetStatusBar();
            }
        }
        get => currentApp.UserAppTheme == AppTheme.Dark || currentApp.RequestedTheme == AppTheme.Dark;
    }
    public bool UseLocation => App.UseLocation;
    public Location? AppLocation => App.MyLocation;
    #region Fake Location Management (Debug Only)
    [RelayCommand]
    private async Task SetFakeLocation()
    {
        FakeLocationMapSettings.VenueLocation = FakeLocation ?? App.MyLocation;
        await App.PushAsync(Routes.MapPage, "MapSettings", FakeLocationMapSettings);
    }

    [RelayCommand]
    private void ClearFakeLocation() => FakeLocation = null;
    public MapSettings FakeLocationMapSettings { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFakeLocationChangeable))]
    public partial bool UseFakeLocation { get; set; }
    async partial void OnUseFakeLocationChanged(bool value)
    {
        if (value)
        {
            if (App.UseFakeLocation)
            {
                // Nothing to do we're already using fake location
            }
            else if (FakeLocation is null)
            {
                await Utilities.ShowAppSnackBarAsync("Please set a fake location first");
                UseFakeLocation = false;
            }
            else
            {
                IsSwitchingToFakeLocation = true;
                await Utilities.ShowAppSnackBarAsync("Will use fake location in 20s"); // Message shows for about 3 seconds
                await Task.Delay(17_000);
                App.UseFakeLocation = true; // Start using the fake location
                await App.RefreshLocationAsync();
                await Utilities.ShowAppSnackBarAsync("Fake location in use");
                IsSwitchingToFakeLocation = false;
            }
        }
        else
        {
            App.UseFakeLocation = false; // Stop using the fake location
            await App.RefreshLocationAsync();
        }
    }

    [ObservableProperty]
    public partial bool IsSwitchingToFakeLocation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFakeLocationChangeable))]
    public partial Location? FakeLocation { get; set; }
    partial void OnFakeLocationChanged(Location? value)
    {
        App.FakeLocation = value;
        if (value is null)
            UseFakeLocation = false;
    }

    public bool IsFakeLocationChangeable => UseFakeLocation || (FakeLocation is not null && !FakeLocation.IsVeryCloseTo(App.GpsLocation));
    #endregion
}