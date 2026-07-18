using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.InAppBilling;
using DivisiBill.Services;

namespace DivisiBill.ViewModels;

/// <summary>
/// ViewModel for the Subscription page that manages subscription status and related operations.
/// Handles displaying subscription information, managing upgrades, and license details.
/// </summary>
public partial class SubscriptionViewModel : ObservableObject
{
    #region Initialization
    private readonly IInAppBilling inAppBilling;
    public SubscriptionViewModel()
    {
        inAppBilling = CrossInAppBilling.Current;
        _ = LoadPrices();
    }

    private async Task LoadPrices()
    {
        await LoadSubscriptionPrice();
        await LoadOcrPrice();
        bool statusChecked = await CallWs.StatusAsync();
        if (statusChecked && !string.IsNullOrEmpty(CallWs.MostRecentStatusInfo))
        {
            // extract from json the number of scans per OCR license
            int? scansPerOcr = Utilities.GetJsonFieldValue<int>(CallWs.MostRecentStatusInfo ??= "{}", "ocrLicenseScans");
            if (scansPerOcr.HasValue)
            {
                ScansPerOcr = scansPerOcr.Value;
            }
        }
        else
        {
            Utilities.DebugMsg("Failed to get status from server");
        }
    }
    public void Refresh()
    {
        IsLimited = App.IsLimited;
    }
    #endregion
    #region Properties
    [ObservableProperty]
    public partial string? SubscriptionPrice { get; set; } = "Loading...";

    private async Task LoadSubscriptionPrice()
    {
        try
        {
            SubscriptionPrice = await Billing.GetItemPriceAsync(Billing.ProSubscriptionId, ItemType.Subscription);
        }
        catch (Exception ex)
        {
            Utilities.DebugMsg($"Failed to load pricing: {ex.Message}");
            SubscriptionPrice = "Contact Support";
        }
        SubscriptionPrice ??= $"{0.99:C}"; // Safe default value, in case server value missing or invalid
    }
    [ObservableProperty]
    public partial string? OcrPrice { get; set; } = "Loading...";

    private async Task LoadOcrPrice()
    {
        try
        {
            OcrPrice = await Billing.GetItemPriceAsync(Billing.OcrLicenseProductId, ItemType.InAppPurchase);
        }
        catch (Exception ex)
        {
            Utilities.DebugMsg($"Failed to load pricing: {ex.Message}");
            OcrPrice = "Contact Support";
        }
        OcrPrice ??= $"{0.99:C}"; // Safe default value, in case server value missing or invalid
    }
    [ObservableProperty]
    public partial bool IsLimited { get; set; }

    partial void OnIsLimitedChanged(bool value)
    {
        App.IsLimited = value;
    }

    [ObservableProperty]
    public partial int ScansPerOcr { get; private set; } = 30; // Safe default value, will be updated from server status
    public bool WsUriDefined => App.WsUriDefined;
    public bool LicenseChecked => App.LicenseChecked;
    public bool HasProSubscription => Billing.ProPurchase is not null;
    public bool InvalidProSubscription => Billing.ProPurchase is not null && Billing.ProPurchase.State != InAppBilling.PurchaseState.Purchased;
    public string? ProSubscriptionId => Billing.ProPurchase?.Id;
    public int ScansLeft => Billing.ScansLeft;
    public bool HasOcrLicense => Billing.OcrPurchase is not null;
    public bool InvalidOcrLicense => Billing.OcrPurchase is not null && Billing.OcrPurchase.State != InAppBilling.PurchaseState.Purchased;
    public string? OcrLicenseId => Billing.OcrPurchase?.Id;
    public int ScansWarningLevel => Billing.ScansWarningLevel;
    public bool IsOcrPurchaseAllowed => ScansLeft < ScansWarningLevel; // Includes the case where the user has purchased no scans yet
    #endregion
    #region Commands
    [RelayCommand]
    private async Task PurchaseProSubscriptionAsync()
    {
        if (Billing.HasOldProProductId)
        {
            await Utilities.DisplayAlertAsync("Tester", "You have a perpetual professional license and do not need a subscription");
            return;
        }
        App.isPurchasing = true; // Avoid the "professional license lost" warning on returning
        App.Settings.HadProSubscription = true; // Avoid the "professional license found" warning on returning to the app, the user already knows they purchased a subscription
        bool subscriptionPurchased = await Billing.PurchaseProSubscriptionAsync();
        App.isPurchasing = false;
        Utilities.DebugMsg("In PurchaseUpgradeAsync, PurchaseProSubscriptionAsync returned " + subscriptionPurchased);
        IsLimited = !subscriptionPurchased;
        if (IsLimited)
        {
            await Utilities.DisplayAlertAsync("Error", "The purchase failed. You did not acquire a professional subscription");
            App.Settings.HadProSubscription = false; // Avoid the "professional license lost" warning on app restart, the user already knows the purchase failed
        }
        else
        {
            await Utilities.DisplayAlertAsync("Thank You",
                $"You have purchased a professional subscription. You may now set the 'Allow Cloud Backup' option.");
            await App.PopAsync(); // Go back to the Settings page
        }
    }

    /// <summary>
    /// Opens the subscription management page or store listing.
    /// </summary>
    [RelayCommand]
    private async Task ManageSubscription()
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

    private async Task PurchaseProLicenseAsync()
    {
        if (Utilities.IsWinUI)
        {
            await Utilities.DisplayAlertAsync("Not Supported", "In-app purchases are not supported on Windows");
            return;
        }
        App.Settings.HadProSubscription = true; // Avoid the "professional license found" warning on returning
        bool licensePurchased = await Billing.PurchaseProLicenseAsync();
        Utilities.DebugMsg("In PurchaseProLicenseAsync, PurchaseProSubscriptionAsync returned " + licensePurchased);
        IsLimited = !licensePurchased;
        if (IsLimited)
            await Utilities.DisplayAlertAsync("Error", "The purchase failed. You did not acquire a professional license");
        else
        {
            await Utilities.DisplayAlertAsync("Thank You",
                $"You have purchased a professional license. You may now set the 'Allow Cloud Backup' option.");
        }
    }

    [RelayCommand]
    private async Task PurchaseOcrScansAsync()
    {
        int scans = await Billing.PurchaseOcrLicenseAsync();
        Utilities.DebugMsg("OCR licenses purchased, total remaining scans = " + scans);
        if (scans == -1)
            await Utilities.DisplayAlertAsync("Error", "The purchase failed. You did not acquire any additional OCR licenses");
        else if (scans < 0)
            await Utilities.DisplayAlertAsync("Error", "The purchase could not be verified. You did not acquire any additional OCR licenses");
        else
            await Utilities.DisplayAlertAsync("Thank You", $"You now have {scans} OCR scans left");
    }
    /// <summary>
    /// Exits the page and does nothing
    /// </summary>
    [RelayCommand]
    private async Task Cancel()
    {
        await App.PopAsync();
    }
    #endregion
}
