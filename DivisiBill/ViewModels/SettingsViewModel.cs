﻿#nullable enable

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
        currentApp = Application.Current is null ? throw new NullReferenceException() : Application.Current;
        ScanOption = 2;
        App.MyLocationChanged += App_MyLocationChanged;
    }

    ~SettingsViewModel()
    {
        App.ProEditionVerified -= App_ProEditionVerified;
        App.MyLocationChanged -= App_MyLocationChanged;
    }

    private void App_MyLocationChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(AppLocation));
    private void App_ProEditionVerified(object? sender, EventArgs e) => RefreshValues();

    public void RefreshValues()
    {
        OnPropertyChanged(nameof(IsLimited));
        OnPropertyChanged(nameof(ScanOption));
        OnPropertyChanged(nameof(LicenseChecked));
        OnPropertyChanged(nameof(HasProSubscription));
        OnPropertyChanged(nameof(InvalidProSubscription));
        OnPropertyChanged(nameof(ProSubscriptionId));
        OnPropertyChanged(nameof(ScansLeft));
        OnPropertyChanged(nameof(IsOcrPurchaseAllowed));
        OnPropertyChanged(nameof(HasOcrLicense));
        OnPropertyChanged(nameof(InvalidOcrLicense));
        OnPropertyChanged(nameof(OcrLicenseId));
        OnPropertyChanged(nameof(Dark));
    }

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
            (currentApp.Resources["CloudViewModel"] as CloudViewModel)?.NotifyProPurchase();
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
    public bool SendCrashYes
    {
        get => App.Settings.SendCrashYes;
        set => App.Settings.SendCrashYes = value;
    }
    public bool SendCrashAsk
    {
        get => App.Settings.SendCrashAsk;
        set => App.Settings.SendCrashAsk = value;
    }
    public bool WsAllowed => App.WsAllowed;
    public bool LicenseChecked => App.LicenseChecked;
    public bool HasProSubscription => Billing.ProPurchase is not null;
    public bool InvalidProSubscription => Billing.ProPurchase is not null && Billing.ProPurchase.State != Plugin.InAppBilling.PurchaseState.Purchased;
    public string? ProSubscriptionId => Billing.ProPurchase?.Id;
    public int ScansLeft => Billing.ScansLeft;
    public bool HasOcrLicense => Billing.OcrPurchase is not null;
    public bool InvalidOcrLicense => Billing.OcrPurchase is not null && Billing.OcrPurchase.State != Plugin.InAppBilling.PurchaseState.Purchased;
    public string? OcrLicenseId => Billing.OcrPurchase?.Id;
    public string BaseAddress => App.WsAllowed ? CallWs.BaseAddress.ToString() : "";
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
}