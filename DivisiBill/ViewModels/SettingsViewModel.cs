#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Services;

namespace DivisiBill.ViewModels;

public partial class SettingsViewModel : ObservableObjectPlus
{
    private readonly Application currentApp;
    public SettingsViewModel()
    {
        if (!App.LicenseChecked)
            App.ProEditionVerified += App_ProEditionVerified;
        FakeLocationMapSettings = new MapSettings("Fake Location", FakeLocation ?? App.MyLocation);
        currentApp = Application.Current is null ? throw new NullReferenceException() : Application.Current;
        ScanOption = 2;
        App.MyLocationChanged += App_MyLocationChanged;
        Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;
        PropertyChanged += SettingsViewModel_PropertyChanged;
    }

    private async void SettingsViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UseFakeLocation))
        {
            if (UseFakeLocation)
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
                    await Utilities.ShowAppSnackBarAsync("Will use fake location in 10s"); // Message shows for about 3 seconds
                    await Task.Delay(7_000);
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
        OnPropertyChanged(nameof(IsLimited));
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

    #region Commands
    [RelayCommand]
    private async Task OpenWebAsync() => await Launcher.OpenAsync(new Uri("https://learn.microsoft.com/en-us/dotnet/maui/what-is-maui"));

    [RelayCommand]
    private async Task OpenAutoPlusAsync() => await Launcher.OpenAsync(new Uri("http://www.autopl.us"));

    [RelayCommand]
    private async Task PurchaseOcrScansAsync()
    {
        IsBusy = true;
        int scans = await Billing.PurchaseOcrLicenseAsync();
        Utilities.DebugMsg("OCR licenses purchased, total remaining scans = " + scans);
        IsBusy = false;
        if (scans == -1)
            await Utilities.DisplayAlertAsync("Error", "The purchase failed. You did not acquire any additional OCR licenses");
        else if (scans < 0)
            await Utilities.DisplayAlertAsync("Error", "The purchase could not be verified. You did not acquire any additional OCR licenses");
        else
            await Utilities.DisplayAlertAsync("Thank You", $"You now have {scans} OCR scans left");
        RefreshValues();
    }

    [RelayCommand]
    private async Task LicensingHelp() => await App.PushAsync($"{Routes.HelpPage}?page=licensing");

    [RelayCommand]
    private async Task PurchaseUpgradeAsync()
    {
        if (Billing.HasOldProProductId)
        {
            await Utilities.DisplayAlertAsync("Tester", "You have a perpetual professional license and do not need a subscription");
            return;
        }
        App.Settings.HadProSubscription = true; // Avoid the "professional license found" warning on returning
        IsBusy = true;
        bool subscriptionPurchased = await Billing.PurchaseProSubscriptionAsync();
        IsBusy = false;
        Utilities.DebugMsg("In PurchaseUpgradeAsync, PurchaseProSubscriptionAsync returned " + subscriptionPurchased);
        IsLimited = !subscriptionPurchased;
        if (IsLimited)
            await Utilities.DisplayAlertAsync("Error", "The purchase failed. You did not acquire a professional subscription");
        else
        {
            await Utilities.DisplayAlertAsync("Thank You",
                $"You have purchased a professional subscription. You may now set the 'Allow Cloud Backup' option.");
            RefreshValues();
        }
    }

    [RelayCommand]
    private async Task RemoveUpgradeAsync()
    {
        if (Billing.HasOldProProductId)
        {
            await Utilities.DisplayAlertAsync("Tester", "You have a perpetual professional license which cannot be modified");
            return;
        }
        await Launcher.OpenAsync(new Uri("https://play.google.com/store/account/subscriptions"
            + $"?sku={Billing.ProSubscriptionId}&package={Billing.ExpectedPackageName}"));
    }

    [RelayCommand]
    private void SystemSettings() => AppInfo.Current.ShowSettingsUI();

    [RelayCommand]
    private void EnableHints() => App.Settings.EnableHints();

    [RelayCommand]
    private void ResetCheckBoxes() => App.Settings.ResetCheckboxes();
    #endregion
    #region Transient Properties
    public bool IsLimited
    {
        get => App.IsLimited;
        set
        {
            if (App.IsLimited != value)
            {
                App.IsLimited = value;
                OnPropertyChanged();
            }
        }
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
            var profiles = Connectivity.ConnectionProfiles;
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
    public bool InvalidProSubscription => Billing.ProPurchase is not null && Billing.ProPurchase.State != Plugin.InAppBilling.PurchaseState.Purchased;
    public string? ProSubscriptionId => Billing.ProPurchase?.Id;
    public int ScansLeft => Billing.ScansLeft;
    public bool HasOcrLicense => Billing.OcrPurchase is not null;
    public bool InvalidOcrLicense => Billing.OcrPurchase is not null && Billing.OcrPurchase.State != Plugin.InAppBilling.PurchaseState.Purchased;
    public string? OcrLicenseId => Billing.OcrPurchase?.Id;
    public string BaseAddress => App.WsUriDefined ? CallWs.BaseAddress.ToString() : "";
    public string LastUse => App.Settings.LastUse.ToString();
    public bool Dark
    {
        set
        {
            if (value != Dark)
                currentApp.UserAppTheme = value ? AppTheme.Dark : AppTheme.Light;
        }
        get => currentApp.UserAppTheme == AppTheme.Dark || currentApp.RequestedTheme == AppTheme.Dark;
    }
    public bool UseLocation => App.UseLocation;
    public Location AppLocation => App.MyLocation;
    #region Fake Location Management (Debug Only)
    [RelayCommand]
    private async Task SetFakeLocation()
    {
        if (Utilities.IsWinUI)
        {
            await Utilities.ShowAppSnackBarAsync("Map is not available on Windows");
            return;
        }
        FakeLocationMapSettings.VenueLocation = FakeLocation ?? App.MyLocation;
        await App.PushAsync(Routes.MapPage, "MapSettings", FakeLocationMapSettings);
    }

    [RelayCommand]
    private void ClearFakeLocation() => FakeLocation = null;
    public MapSettings FakeLocationMapSettings { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFakeLocationChangeable))]
    public partial bool UseFakeLocation { get; set; }

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