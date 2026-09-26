using DivisiBill.InAppBilling;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DivisiBill.Services;

/// <summary>
/// <para>The billing class handles matters related to in-app billing. It relies on the former InAppBilling plug-in
/// (now the DivisiBill.InAppBilling folder) and the DivisiBill web service. For the purposes of this
/// discussion the purchase may be for an in-app product or a subscription.</para>
/// 
/// <para>Professional purchases (usually subscriptions) enable cloud features, OCR purchases enable scans, 
/// you can buy an OCR purchase whenever your scan counts drop below a small threshold. Buying one adds a
/// fixed number of scans which then decrement as you perform OCR scans on individual bills. When the scan count
/// reaches zero we notify the store that the purchase has been consumed and you must buy another before more 
/// scans are allowed. The tracking is mostly done by the web service, but we keep a local copy of how many scans
/// we think are left for convenience, even though the web service value is definitive.</para>
/// 
/// <para>The flow is that a user buys a license through the program using the store for the platform (only Android at
/// present) and then presents it to a web service for validation. The web service ALSO calls the store to validate the
/// would-be license was issued by the store and not yet acknowledged. We check the license signature early in both the
/// client and server because if that check fails there's no need to go on and do expensive web service calls, the license
/// can be rejected. The web service also checks that the license is not yet in its list of known licenses. If that
/// validation passes, the license is stored in a table (so it's now a known one) and a value is returned to the caller
/// to tell it to acknowledge the license with the store. If it is an OCR license we also return a count of OCR scans it
/// enables the user to consume and persist the new total (including unused scans) from any previous licenses for the same
/// purchaser.</para>
/// 
/// <para>In the unlikely event that a purchase is interrupted in the middle the user might end up with a legitimate license
/// we've never seen. In that case the license is added to our store just as if it had gone through the normal purchase flow.</para>
/// 
/// <para>The pro license is checked at startup and intermittently thereafter <see cref="GetHasProSubscriptionAsync"/>. 
/// The OCR license management is mostly in the web service but it is occasionally checked 
/// <see cref="GetHasOcrLicenseAsync"/> against the web service to get the count of remaining scans for that license. 
/// The number of scans left is decremented whenever a scan is done by the web service and once the number left drops below a
/// threshold <see cref="ScansWarningLevel"/> the user is allowed to purchase a new license and we delete the old one (on 
/// Android you have to do that before you can buy another), see <see cref="ConsumeDepletedOcrLicense"/>. Any scans remaining on 
/// the old license are transferred to the new license.</para>
/// 
/// <para>If someone could get hold of a valid license and persuade an app to present it, they could scan bills and decrement 
/// the remaining scans on that license. This is exactly what we do for testing on Windows but that logic is only present in 
/// debug builds. Getting hold of a valid license from a release version of the code would be quite hard in that you'd need
/// to reach inside the DivisiBill code or decode HTTPS messages to do it. So not impossible, but a lot of work, which is the
/// point of all this (to make it more work than it is worth).</para>
/// 
/// <para>Because only a valid but previously unknown license ever allocates additional scans there's no obvious way to reuse
/// a license to get more scans, all you can do is consume the ones you've already been allocated. The Play Store for Android 
/// adds the additional wrinkle of acknowledging a license, but that's not necessary for the security of the process.</para>
/// </summary>
internal static class Billing
{
    #region Shared Code and Data
    /// <summary>
    /// The status of a billing operation, typically returned from a method that performs some billing operation.
    /// </summary>
    public readonly union StringOrBillingStatus(string, Billing.BillingStatusType);
    public readonly union InAppBillingPurchaseOrBillingStatus(InAppBillingPurchase, Billing.BillingStatusType);
    public readonly union IInAppBillingOrBillingStatus(IInAppBilling, Billing.BillingStatusType);
    public enum BillingStatusType
    {
        ok,
        notFound,
        notLicensing,
        notVerified, // Anything larger than this is a connection issue
        noInternet,
        connectionFailed,
        connectionFaulted,
    }
    public const string ExpectedPackageName = "com.autoplus.divisibill";
    public const int ScansWarningLevel = 4; // If this many or fewer are left, warn the user and allow them to purchase additional scans

    private static string? GetJsonFieldValue(string jsonString, string fieldName) => JsonDocument.Parse(jsonString).RootElement.TryGetProperty(fieldName, out JsonElement fieldValue) ? fieldValue.GetString() : string.Empty;
    #endregion
    #region Pro License
    public const string ProSubscriptionId = "pro.subscription";
    public const string OldProProductId = "pro.upgrade"; // a product, not a subscription, kept around to simplify testing because it does not expire
    /// <summary>
    /// The last known Pro purchase, either a subscription or a product. This is set by <see cref="GetHasProSubscriptionAsync"/> but
    /// may be null if no purchase has been found. If the purchase could not be verified it will be set but its state will be PurchaseState.Failed.
    /// If the purchase was found and verified it will be set and its state will be PurchaseState.Purchased.
    /// </summary>
    internal static InAppBillingPurchase? ProPurchase { get; private set; } = null;
    internal static bool HasOldProProductId { get; private set; } = false;

    /// <summary>
    /// Check the license service for a pro subscription (or the test pro product) and return a value indicating the outcome.
    /// </summary>
    /// <returns>
    /// One of the BillingStatus enumerated types
    /// <list type="table">
    ///  <item>ok - everything worked, the subscription is good</item>
    ///  <item>notFound - no evidence of the subscription - normal for users who have not purchased it</item>
    ///  <item>notVerified - we found a subscription (Android handed us one when asked) but could not verify it was legitimate</item>
    /// </list>
    /// </returns>
    internal static async Task<BillingStatusType> GetHasProSubscriptionAsync()
    {
        ProPurchase = null; // For safety because whatever we had before is irrelevant
        HasOldProProductId = false; // For safety because whatever we had before is irrelevant
#if DEBUG
        if (DeviceInfo.Platform == DevicePlatform.WinUI)
        {
            if (string.IsNullOrWhiteSpace(Generated.BuildInfo.DivisiBillTestProJsonB64))
            {
                Utilities.DebugMsg("In GetHasProSubscriptionAsync, DivisiBillTestProJsonB64 was empty");
                ProPurchase = new InAppBillingPurchase() { State = PurchaseState.Failed };
                return BillingStatusType.notLicensing; // a specific error so it can be handled silently 
            }
            else
            {
                string json = Encoding.UTF8.GetString(Convert.FromBase64String(Generated.BuildInfo.DivisiBillTestProJsonB64));
                string signatureB64 = Generated.BuildInfo.DivisiBillTestProSignatureB64;
                var fakeRes = await GetInAppBillingPurchaseFakeAsync(json, signatureB64);
                switch (fakeRes)
                {
                    case string resultString:
                        ProPurchase = new InAppBillingPurchase()
                        {
                            ProductId = OldProProductId, // temporary
                            State = PurchaseState.Failed,
                            Id = GetJsonFieldValue(json, "orderId"),
                            ObfuscatedAccountId = GetJsonFieldValue(json, "obfuscatedAccountId"),
                            Signature = signatureB64,
                            OriginalJson = json
                        };
                        if (int.TryParse(resultString, out int scans) && scans >= 0)
                        {
                            ProPurchase.State = PurchaseState.Purchased;
                            HasOldProProductId = true;
                            return BillingStatusType.ok; // No error
                        }
                        else
                        {
                            Utilities.DebugMsg("In GetHasProSubscriptionAsync, failed to parse scans from web service result: " + resultString);
                            return BillingStatusType.notVerified;
                        }
                    case Billing.BillingStatusType fakeFail:
                        Utilities.DebugMsg("In GetHasProSubscriptionAsync, GetInAppBillingPurchaseFakeAsync returned " + fakeFail);
                        return fakeFail;
                }
            }
        }
        else
#endif
            if (DeviceInfo.Current.Platform == DevicePlatform.Android)
            {
                Billing.BillingStatusType billingResultOld = BillingStatusType.notFound;
                try
                {
                    #region Pro Product (used primarily for Testing)
                    Utilities.DebugMsg("In GetHasProSubscriptionAsync, trying pro product");
                    var oldRes = await GetInAppBillingPurchaseAsync(OldProProductId, isSubscription: false);
                    switch (oldRes)
                    {
                        case Billing.BillingStatusType billingStatus:
                            Utilities.DebugMsg("In GetHasProSubscriptionAsync, did not find pro product, result = " + billingStatus);
                            if (billingStatus >= BillingStatusType.noInternet) // A catastrophic error, no point going on
                                return billingStatus;
                            else
                                billingResultOld = billingStatus; // Remember it for later
                            break;
                        case InAppBillingPurchase billingPurchase:
                            HasOldProProductId = true;
                            ProPurchase = billingPurchase;
                            if (billingPurchase.State == PurchaseState.Purchased)
                            {
                                Utilities.DebugMsg("Exiting GetHasProSubscriptionAsync, found old style pro product " + billingPurchase.Id);
                                billingResultOld = BillingStatusType.ok;
                                return BillingStatusType.ok; // No error
                            }
                            else
                            {
                                Utilities.DebugMsg("Exiting GetHasProSubscriptionAsync, found pro product not purchased " + billingPurchase.Id);
                                billingResultOld = BillingStatusType.notVerified;
                                // Don't return yet, we want to check for a subscription too
                            }
                            break;
                    }
                    Utilities.DebugMsg("In GetHasProSubscriptionAsync, did not find usable pro product");
                    #endregion
                    #region Pro Subscription
                    Utilities.DebugMsg("In GetHasProSubscriptionAsync, awaiting GetInAppBillingPurchaseAsync(ProSubscriptionId)");
                    var newRes = await GetInAppBillingPurchaseAsync(ProSubscriptionId, isSubscription: true);
                    switch (newRes)
                    {
                        case Billing.BillingStatusType billingStatus:
                            Utilities.DebugMsg("In GetHasProSubscriptionAsync, did not find pro subscription, result = " + billingStatus);
                            // If the old style license was not found (the normal case) return the status of the subscription
                            return billingResultOld == BillingStatusType.notFound ? billingStatus : billingResultOld;
                        case InAppBillingPurchase billingPurchase:
                            if (billingPurchase.State == PurchaseState.Purchased)
                            {
                                Utilities.DebugMsg("Exiting GetHasProSubscriptionAsync, found pro subscription " + billingPurchase.Id);
                                ProPurchase = billingPurchase;
                                return BillingStatusType.ok; // No error
                            }
                            break;
                    }
                    #endregion
                }
                catch (Exception ex)
                {
                    Utilities.DebugMsg("In GetHasProSubscriptionAsync, threw an exception:" + ex);
                }
            }
            else
                Utilities.DebugMsg("In GetHasProSubscriptionAsync, unsupported environment, treated as NO PRO SUBSCRIPTION was found");

        return BillingStatusType.notFound;
    }
    /// <summary>
    /// Purchase a Pro license from an app store then check it against our web service to make sure it is legitimate.
    /// </summary>
    /// <returns>True if purchase worked, false otherwise</returns>
    internal static async Task<bool> PurchaseProSubscriptionAsync()
    {
        Debug.Assert(App.Settings is not null);
        ProPurchase = await PurchaseItemAsync(ProSubscriptionId, App.Settings.UserKey, isSubscription: true);
        App.RequireCheckForProEdition(); // So that the next check will be a thorough one and will pick up any subscription change
        if (ProPurchase is null)
            Utilities.DebugMsg("In Billing.PurchaseProSubscriptionAsync, PurchaseItemAsync returned null");
        else
        {
            string? validationResult = await CallWs.VerifyPurchase(ProPurchase);

            if (validationResult is null)
                Utilities.DebugMsg("In Billing.PurchaseProSubscriptionAsync, CallWs.VerifyPurchase returned null");
            else if (!int.TryParse(validationResult, out int result))
                Utilities.DebugMsg($"In Billing.PurchaseProSubscriptionAsync, CallWs.VerifyPurchase did not return an integer ({validationResult}), meaning error");
            else if (result < 0)
                Utilities.DebugMsg($"In Billing.PurchaseProSubscriptionAsync, CallWs.VerifyPurchase returned a negative number {result}, meaning error");
            else
                return true;
        }
        Utilities.DebugMsg("Returning FALSE from Billing.PurchaseProSubscriptionAsync");
        return false;
    }
    #endregion
    #region Old Pro Product Purchase (for testing only)
    /// <summary>
    /// Check whether the user has a Pro license, if it is valid, and if it has scans left
    /// </summary>
    /// <returns>Scans remaining or a negative number if the license was invalid</returns>
    internal static async Task<BillingStatusType> GetHasProLicenseAsync()
    {
        ProPurchase = null; // For safety because whatever we had before is irrelevant
#if DEBUG
        if (DeviceInfo.Platform == DevicePlatform.WinUI)
        {
            if (string.IsNullOrWhiteSpace(Generated.BuildInfo.DivisiBillTestProJsonB64))
            {
                Utilities.DebugMsg("In GetHasProLicenseAsync, DivisiBillTestProJsonB64 was empty");
                ProPurchase = new InAppBillingPurchase() { State = PurchaseState.Failed };
                return BillingStatusType.notLicensing; // a specific error so it can be handled silently 
            }
            else
            {
                string json = Encoding.UTF8.GetString(Convert.FromBase64String(Generated.BuildInfo.DivisiBillTestProJsonB64));
                string signatureB64 = Generated.BuildInfo.DivisiBillTestProSignatureB64;
                var fakeRes = await GetInAppBillingPurchaseFakeAsync(json, signatureB64);
                switch (fakeRes)
                {
                    case string resultString:
                        ProPurchase = new InAppBillingPurchase()
                        {
                            ProductId = OldProProductId, // temporary
                            State = PurchaseState.Failed,
                            Id = GetJsonFieldValue(json, "orderId"),
                            ObfuscatedAccountId = GetJsonFieldValue(json, "obfuscatedAccountId"),
                            Signature = signatureB64,
                            OriginalJson = json
                        };
                        if (int.TryParse(resultString, out int scans) && scans >= 0)
                        {
                            ProPurchase.State = PurchaseState.Purchased;
                            HasOldProProductId = true;
                            return BillingStatusType.ok; // No error
                        }
                        else
                            return BillingStatusType.notVerified;
                    case Billing.BillingStatusType fakeFail:
                        return fakeFail;
                }
            }
        }
        else
#endif
            if (DeviceInfo.Current.Platform == DevicePlatform.Android)
            {
                try
                {
                    #region Old Style Pro Product (used for Testing)
                    Utilities.DebugMsg("In GetHasProLicenseAsync, trying old style pro product");
                    var oldProductRes = await GetInAppBillingPurchaseAsync(OldProProductId, isSubscription: false);
                    switch (oldProductRes)
                    {
                        case InAppBillingPurchase oldProdSucc:
                            if (oldProdSucc.State == PurchaseState.Purchased)
                            {
                                Utilities.DebugMsg("Exiting GetHasProLicenseAsync, found old style pro product " + oldProdSucc.Id);
                                ProPurchase = oldProdSucc;
                                HasOldProProductId = true;
                                return BillingStatusType.ok; // No error
                            }
                            else
                            {
                                Utilities.DebugMsg("In GetHasProLicenseAsync, found old style pro product but state was " + oldProdSucc.State);
                                return BillingStatusType.notFound;
                            }
                        case Billing.BillingStatusType oldProdFail:
                            Utilities.DebugMsg("In GetHasProLicenseAsync, did not find old style pro product");
                            return oldProdFail;
                    }
                    ;
                    #endregion
                }
                catch (Exception ex)
                {
                    Utilities.DebugMsg("In GetHasProLicenseAsync, threw an exception:" + ex);
                }
                // If something went wrong with the old style license check report a fault
                return BillingStatusType.connectionFaulted;
            }
            else
            {
                Utilities.DebugMsg("In GetHasProLicenseAsync, unsupported environment, treated as NO PRO LICENSE was found");
            }
        return BillingStatusType.notFound;
    }

    /// <summary>
    /// Purchase a pro license from an app store then check it against our web service to make sure it is legitimate.
    /// </summary>
    /// <returns>Scans remaining or a negative number if the purchase failed</returns>
    internal static async Task<bool> PurchaseProLicenseAsync()
    {
        Debug.Assert(App.Settings is not null);
        if (await GetHasProLicenseAsync() == BillingStatusType.ok)
            await ConsumeProLicenseAsync();
        ProPurchase = await PurchaseItemAsync(OldProProductId, App.Settings.UserKey);
        if (ProPurchase is null)
            Utilities.DebugMsg("In Billing.PurchaseProLicenseAsync, PurchaseItemAsync returned null");
        else
        {
            string? validationResult = await CallWs.VerifyPurchase(ProPurchase);
            if (validationResult is null)
                Utilities.DebugMsg("In Billing.PurchaseProLicenseAsync, CallWs.VerifyPurchase returned null");
            else if (!int.TryParse(validationResult, out int result))
                Utilities.DebugMsg($"In Billing.PurchaseProLicenseAsync, CallWs.VerifyPurchase did not return an integer ({validationResult}), meaning error");
            else if (result < 0)
                Utilities.DebugMsg($"In Billing.PurchaseProLicenseAsync, CallWs.VerifyPurchase returned a negative number {result}, meaning error");
            else
            {
                HasOldProProductId = true;
                return true;
            }
        }
        Utilities.DebugMsg("Returning FALSE from Billing.PurchaseProLicenseAsync");
        return false;
    }

    /// <summary>
    /// Remove a Pro license from the store (but not from our list of used licenses). Usually because it is being replaced.
    /// </summary>
    internal static async Task<bool> ConsumeProLicenseAsync()
    {
        if (Utilities.IsWinUI)
            return false; // Not implemented for Windows 
        BillingStatusType test = await GetHasProLicenseAsync();
        Utilities.DebugMsg("In ConsumeDepletedProLicense, license purchase test returned " + test);
        if (ProPurchase is not null && ProPurchase.ProductId is not null && ProPurchase.PurchaseToken is not null)
        {
            // Notify the store that it can forget about this item, and allow the user to purchase another.
            bool consumed = await ConsumeItemAsync(ProPurchase.ProductId, ProPurchase.PurchaseToken);
            if (consumed)
            {
                Utilities.DebugMsg("In ConsumeDepletedProLicense, consumed a pro license, Order ID = " + ProPurchase.Id);
                HasOldProProductId = false;
                ProPurchase = null;
            }
            else
                Utilities.DebugMsg("In ConsumeDepletedProLicense, failed to consume a license, Order ID = " + ProPurchase.Id);
            return consumed;
        }
        return false;
    }
    #endregion
    #region OCR License
    public static readonly string OcrLicenseProductId = "ocr.calls";

    /// <summary>
    /// The number of scans left for the current OCR license, the web service is the definitive source of this information
    /// but we keep a local copy for convenience. The local copy is set by <see cref="GetHasOcrLicenseAsync"/> and updated by
    /// <see cref="CallWs.ImageToScannedBill(Stream, CancellationToken)"/> when a scan is performed. The value is decremented
    /// by the web service whenever a scan is performed successfully.
    /// </summary>
    internal static int ScansLeft { get; set; }

    /// <summary>
    /// The last known OCR purchase. This is set by <see cref="GetHasOcrLicenseAsync"/> but may be null if no purchase has
    /// been found. If the purchase exists but could not be verified it will be set but its state will be PurchaseState.Failed.
    /// </summary>
    internal static InAppBillingPurchase? OcrPurchase { get; private set; } = null;
    /// <summary>
    /// Check whether the user has an OCR license, if it is valid, and if it has scans left
    /// </summary>
    /// <returns>Scans remaining or a negative number if the license was invalid</returns>
    internal static async Task<int> GetHasOcrLicenseAsync()
    {
#if DEBUG
        if (DeviceInfo.Platform == DevicePlatform.WinUI)
        {
            if (string.IsNullOrWhiteSpace(Generated.BuildInfo.DivisiBillTestOcrJsonB64))
            {
                Utilities.DebugMsg("In GetHasProSubscriptionAsync, DivisiBillTestProJsonB64 was empty");
                OcrPurchase = new InAppBillingPurchase() { State = PurchaseState.Failed };
                return -1; // error 
            }
            else
            {
                string json = Encoding.UTF8.GetString(Convert.FromBase64String(Generated.BuildInfo.DivisiBillTestOcrJsonB64));
                string signatureB64 = Generated.BuildInfo.DivisiBillTestOcrSignatureB64;
                var fakeRes2 = await GetInAppBillingPurchaseFakeAsync(json, signatureB64);
                if (fakeRes2 is string resultString)
                {
                    OcrPurchase = new InAppBillingPurchase() // Set regardless of whether verification works or fails
                    {
                        ProductId = OcrLicenseProductId,
                        State = PurchaseState.Failed,
                        Id = GetJsonFieldValue(json, "orderId"),
                        ObfuscatedAccountId = GetJsonFieldValue(json, "obfuscatedAccountId"),
                        Signature = signatureB64,
                        OriginalJson = json
                    };
                    if (resultString is not null && int.TryParse(resultString, out int scans))
                    {
                        OcrPurchase.State = PurchaseState.Purchased;
                        ScansLeft = scans;
                        return scans;
                    }
                }
                else
                    return -2; // Error
            }
        }
        else
#endif
            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                Utilities.DebugMsg($"In GetHasOcrLicenseAsync, awaiting GetInAppBillingPurchaseAsync(\"{OcrLicenseProductId}\")");
                var ocrRes = await GetInAppBillingPurchaseAsync(OcrLicenseProductId);
                switch (ocrRes)
                {
                    case InAppBillingPurchase gotOcr:
                        if (gotOcr.State == PurchaseState.Purchased)
                        {
                            OcrPurchase = gotOcr;
                            ScansLeft = OcrPurchase.Quantity; // The value returned by calling the DivisiBill web service
                            Utilities.DebugMsg("Exiting GetHasOcrLicenseAsync, returning " + ScansLeft);
                            return ScansLeft;
                        }
                        else
                            Utilities.DebugMsg("Exiting GetHasOcrLicenseAsync, purchase state was " + gotOcr.State);
                        break;
                    case Billing.BillingStatusType ocrFail:
                        Utilities.DebugMsg("In GetHasOcrLicenseAsync, GetInAppBillingPurchaseAsync returned a purchase with state " + ocrFail);
                        break;
                }
                ScansLeft = 0;
            }
        return -1;
    }

    internal readonly union IntOrString(int, string);

    /// <summary>
    /// Purchase an OCR license from an app store then check it against our web service to make sure it is legitimate.
    /// </summary>
    /// <returns>Scans remaining or a negative number if the purchase failed</returns>
    internal static async Task<IntOrString> PurchaseOcrLicenseAsync()
    {
        Debug.Assert(App.Settings is not null);
        if (await GetHasOcrLicenseAsync() < ScansWarningLevel)
            await ConsumeDepletedOcrLicense(); // If we have a license with too few scans left, consume it so the user can buy another
        OcrPurchase = await PurchaseItemAsync(OcrLicenseProductId, App.Settings.UserKey);
        IntOrString result;
        if (OcrPurchase is null)
            result = "OcrPurchase is null";
        else
        {
            string? validationResult = await CallWs.VerifyPurchase(OcrPurchase);
            result = validationResult is null || !int.TryParse(validationResult, out int parsedResult)
                ? "validationResult is null or not an integer"
                : parsedResult == -408 ? "no internet"
                : parsedResult;
        }
        if (result is string s)
            Utilities.DebugMsg($"In PurchaseOcrLicenseAsync, purchase failed: \"{s}\", unusable scans left = {ScansLeft}");
        else if (result is int intResult)
            Utilities.DebugMsg($"In PurchaseOcrLicenseAsync, OCR scans purchased = {intResult}, scans left = {ScansLeft}");
        return result;
    }
    /// <summary>
    /// Remove an OCR license from the store (but not from our list of used licenses) once it has too few scans, 
    /// so the user can buy another.
    /// </summary>
    internal static async Task ConsumeDepletedOcrLicense()
    {
        if (Utilities.IsWinUI)
            return; // Not implemented for Windows 
        int purchaseCount = await GetHasOcrLicenseAsync();
        Utilities.DebugMsg("In ConsumeDepletedOcrLicense, license purchase test returned " + purchaseCount);
        if (purchaseCount >= ScansWarningLevel) // Too many scans associated with this license have not yet been consumed
            Utilities.DebugMsg($"In ConsumeDepletedOcrLicense, because license still has {purchaseCount} scans it will not be removed");
        else if (OcrPurchase is not null && OcrPurchase.ProductId is not null && OcrPurchase.PurchaseToken is not null)
        {
            // Notify the store that it can forget about this item, and allow the user to purchase another.
            await ConsumeItemAsync(OcrPurchase.ProductId, OcrPurchase.PurchaseToken);
            if (purchaseCount < 0) // We've never seen this license, but remove it anyway because it prevents the user buying another
                Utilities.DebugMsg("In ConsumeDepletedOcrLicense, consumed an unrecognized license, Order ID = " + OcrPurchase.Id);
            else
                Utilities.DebugMsg("In ConsumeDepletedOcrLicense, consumed a license, Order ID = " + OcrPurchase.Id);
            OcrPurchase = null;
        }
    }
    #endregion
    #region Communication with App Store
    #region Connection Management
    public static int BillingConnections = 0;
    private static async Task<IInAppBillingOrBillingStatus> OpenBilling([CallerMemberName] string methodName = "UnknownMethod")
    {
        Utilities.DebugMsg($"In OpenBilling: Called from {methodName}: BillingConnections = {BillingConnections}");
        if (BillingConnections == 0)
        {
            try
            {
                await CrossInAppBilling.Current.ConnectAsync();
            }
            catch (Exception ex)
            {
                Utilities.DebugMsg($"In OpenBilling: Fault awaiting CrossInAppBilling.Current.ConnectAsync, exception = {ex}");
                return BillingStatusType.connectionFaulted;
            }
        }
        if (CrossInAppBilling.Current.IsConnected)
        {
            Utilities.DebugMsg("In OpenBilling: Connected to CrossInAppBilling");
            BillingConnections++;
            return CrossInAppBilling.Current;
        }
        else
        {
            Utilities.DebugMsg("In OpenBilling: Could not connect to CrossInAppBilling, returning null");
            return BillingStatusType.connectionFailed;
        }
    }
    private static async Task CloseBilling([CallerMemberName] string methodName = "UnknownMethod")
    {
        Utilities.DebugMsg($"In CloseBilling called from {methodName}: BillingConnections = {BillingConnections}");
        BillingConnections--;
        if (BillingConnections == 0)
        {
            await CrossInAppBilling.Current.DisconnectAsync();
        }
    }
    #endregion
    #region Purchase and Consume Licenses
    /// <summary>
    /// Purchase either a product or a subscription from the app store (just the Google play Store for now).
    /// </summary>
    /// <param name="productId">The ID of the product or subscription to be purchased</param>
    /// <param name="isSubscription">Whether it is a subscription (true) or a one time product license (false)</param>
    /// <returns>The purchase record if the purchase was successful, otherwise null</returns>
    private static async Task<InAppBillingPurchase?> PurchaseItemAsync(string productId, string obfuscatedAccountId, bool isSubscription = false)
    {
        if (Connectivity.NetworkAccess != NetworkAccess.Internet)
        {
            // No Internet, don't even bother trying
            return null;
        }
        InAppBillingPurchase? purchase = null;
        var openRes = await OpenBilling();
        switch (openRes)
        {
            case Billing.BillingStatusType billingStatus:
                Utilities.DebugMsg("In GetItemPriceAsync, OpenBilling returned " + billingStatus);
                return null;
            case IInAppBilling inAppBilling:
                try
                {
                    try
                    {
                        purchase = await inAppBilling.PurchaseAsync(productId, isSubscription ? ItemType.Subscription : ItemType.InAppPurchase, obfuscatedAccountId);
                    }
                    catch (InAppBillingPurchaseException pe)
                    {
                        Utilities.DebugMsg("In PurchaseItemAsync, billing.PurchaseAsync threw a PurchaseException:" + pe.Message + ", " + pe.PurchaseError.ToString());
                    }
                    catch (Exception ex)
                    {
                        Utilities.DebugMsg("In PurchaseItemAsync, billing.PurchaseAsync threw an exception:" + ex);
                    }

                    //possibility that a null came through, perhaps because the user canceled out of the purchase.
                    if (purchase is null)
                        return null;
                    else if (purchase.State == PurchaseState.Purchased)
                    {
                        if (!VerifyDivisiBillPurchaseSignature(purchase))
                        {
                            Utilities.DebugMsg("In Billing.PurchaseItemAsync:  Purchase signature verification failed");
                            return null;
                        }
                        // So the purchase record looks good, now call our web service to record it and make sure it's not being reused
                        bool recorded = await CallWs.RecordPurchaseAsync(purchase);
                        if (recorded)
                        {
                            // The web service recorded the license successfully, so we can consider the purchase complete and acknowledged
                            Utilities.DebugMsg("In Billing.PurchaseItemAsync:  Purchase recorded successfully");
                            // Refresh our local copy of the license with an acknowledged one from the store and send the purchase signature to the web service
                            var refreshRes = await GetInAppBillingPurchaseAsync(productId, isSubscription);
                            switch (refreshRes)
                            {
                                case InAppBillingPurchase refreshed:
                                    Utilities.DebugMsg("In Billing.PurchaseItemAsync:  Purchase refresh successful, Order ID = " + refreshed.Id);
                                    return refreshed;
                                case Billing.BillingStatusType rf:
                                    Utilities.DebugMsg("In Billing.PurchaseItemAsync:  Purchase refresh failed, result = " + rf.ToString());
                                    break;
                            }
                        }
                        else
                        {
                            // Something suspicious happened, we got an alleged new license from Google, but were unable to record it (meaning it was not really new)
                            Utilities.DebugMsg("In Billing.PurchaseItemAsync: Attempt to record license failed");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ex.ReportCrash();
                }
                finally
                {
                    await CloseBilling();
                }
                return null;
            default:
                Utilities.DebugMsg("In GetInAppBillingPurchaseAsync, OpenBilling returned an unexpected type: " + openRes.GetType().Name);
                return null;
        }
    }

    /// <summary>
    /// Consume a license that has been used up, so the user can buy another one, normally used with OCR licenses.
    /// </summary>
    /// <param name="productId">The product the license is for</param>
    /// <param name="purchaseToken">The token provided by the store when issuing the license</param>
    /// <returns>True if the license was consumed, false otherwise</returns>
    private static async Task<bool> ConsumeItemAsync(string productId, string purchaseToken)
    {
        if (Connectivity.NetworkAccess != NetworkAccess.Internet)
        {
            // No Internet, don't even bother trying
            return false;
        }
        var openRes = await OpenBilling();
        switch (openRes)
        {
            case Billing.BillingStatusType billingStatus:
                Utilities.DebugMsg("In ConsumeItemAsync, OpenBilling returned " + billingStatus);
                return false;
            case IInAppBilling Interface:
                try
                {
                    bool consumedItem = await Interface.ConsumePurchaseAsync(productId, purchaseToken);

                    return consumedItem;
                }
                catch (Exception ex)
                {
                    ex.ReportCrash();
                }
                finally
                {
                    await CloseBilling();
                }
                return false;
            default:
                Utilities.DebugMsg("In ConsumeItemAsync, OpenBilling returned an unexpected type: " + openRes.GetType().Name);
                return false;
        }
    }

    /// <summary>
    /// Get the price of a purchase (product or subscription) without initiating a purchase flow.
    /// This is useful for displaying the price of a product before the user decides to buy it.
    /// </summary>
    /// <param name="productId">The product ID</param>
    /// <param name="itemType">The type of item (e.g., subscription, consumable)</param>
    /// <returns>The price of the item, or null if it cannot be retrieved</returns>
    internal static async Task<string?> GetItemPriceAsync(string productId, ItemType itemType)
    {
        if (Connectivity.NetworkAccess != NetworkAccess.Internet)
        {
            // No Internet, don't even bother trying
            return null;
        }
        var openRes = await OpenBilling();
        switch (openRes)
        {
            case Billing.BillingStatusType billingStatus:
                Utilities.DebugMsg("In GetItemPriceAsync, OpenBilling returned " + billingStatus);
                return null;
            case IInAppBilling Interface:
                try
                {
                    string? price = await Interface.GetPriceAsync(productId, itemType);

                    return price;
                }
                catch (Exception ex)
                {
                    ex.ReportCrash();
                }
                finally
                {
                    await CloseBilling();
                }
                return null;
            default:
                Utilities.DebugMsg("In GetInAppBillingPurchaseAsync, OpenBilling returned an unexpected type: " + openRes.GetType().Name);
                return null;
        }
    }
    #endregion
    #region Validate Existing Licenses
    private static bool VerifyDivisiBillPurchaseSignature(string? signedData, string? signature)
    {
        const string divisiBillPublicKeyBase64 = @"MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAgfNwFZUg8fTc0Qd0PizHh+lZyjYJQDx2IH9XXZDE1X" +
            "/aTAp9s5offgtVYkaepHn17UAAHx8d4W6IVUSbtNlAiKxudmEo2tjoSYp6nnSlWRCs7Tzi6t91aMPmgaWUyx9/MCWFj3SRJz9cWhb84JiFDX3UecKKFUyOo+7NzeCvHOCvn" +
            "5JHe+kXMB+wxiYYKcy/vPsOuKlfxkf3GRvWsYJPRLxjB4hWm17HX+vT1AWXZxrLFI1iNiF0WFhYU72zunM7JAla6hUcHag/nFZYHfZxzjAf8YlFCMUbqPTZkINehRHDiM8lg" +
            "brHR5Df32rw+m3cLWKqd5wWqu4yr9+iOHdXzwIDAQAB";

        if (signedData is null || signature is null)
            return false;

        // string signedDataB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(signedData)); // Handy if you need to get hold of the signed data in base64 for testing
        try
        {
            byte[] keyBytes = Convert.FromBase64String(divisiBillPublicKeyBase64);
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(keyBytes, out _);

            byte[] dataBytes = Encoding.UTF8.GetBytes(signedData);
            byte[] signatureBytes = Convert.FromBase64String(signature);

            return rsa.VerifyData(dataBytes, signatureBytes, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }
    private static bool VerifyDivisiBillPurchaseSignature(InAppBillingPurchase purchase) => VerifyDivisiBillPurchaseSignature(purchase.OriginalJson, purchase.Signature);

#if DEBUG
    /// <summary>
    /// A fake version of GetInAppBillingPurchaseAsync that uses a pre-formatted JSON string to simulate a purchase.
    /// Only compiled in DEBUG builds and only used on Windows for testing.
    /// </summary>
    /// <param name="androidJson">JSON representation of a license</param>
    /// <returns></returns>
    private static async Task<StringOrBillingStatus> GetInAppBillingPurchaseFakeAsync(string androidJson, string signatureB64)
    {
        Utilities.DebugMsg("In GetInAppBillingPurchaseFakeAsync");
        var androidJsonObject = JsonNode.Parse(androidJson);
        if (androidJsonObject is null)
        {
            Utilities.DebugMsg("In GetInAppBillingPurchaseFakeAsync, androidJsonObject was null, returning notFound");
            return BillingStatusType.notFound;
        }
        JsonNode? productIdNode = androidJsonObject["productId"];
        if (productIdNode is null || productIdNode.GetValue<string>() is not string productId)
        {
            Utilities.DebugMsg("In GetInAppBillingPurchaseFakeAsync, productIdNode was not a string, returning notFound");
            return BillingStatusType.notFound;
        }
        if (!VerifyDivisiBillPurchaseSignature(androidJson, signatureB64))
        {
            Utilities.DebugMsg("In GetInAppBillingPurchaseFakeAsync, purchase signature was invalid, returning notVerified");
            return BillingStatusType.notVerified;
        }
        try
        {
            InAppBillingPurchase fakePurchase = new() { OriginalJson = androidJson, Signature = signatureB64, ProductId = productId, State = PurchaseState.Purchased };

            string? validationResult = await CallWs.VerifyPurchase(fakePurchase);

            if (validationResult is null)
            {
                Utilities.DebugMsg("In GetInAppBillingPurchaseFakeAsync, VerifyPurchase returned null, returning notFound");
                return BillingStatusType.notFound;
            }
            else if (validationResult == "-408")
            {
                Utilities.DebugMsg("In GetInAppBillingPurchaseFakeAsync, VerifyPurchase returned timeout, returning noInternet");
                return BillingStatusType.noInternet;
            }

            Utilities.DebugMsg($"Exiting GetInAppBillingPurchaseFakeAsync, returning \"{validationResult}\"");

            return validationResult;
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
        }
        return BillingStatusType.notFound;
    }
#endif

    /// <summary>
    /// Get a license object for a named product by checking the store for a record of it, then verifying the returned license with the web service.
    /// </summary>
    /// <param name="productId">The product Id we need a license for</param>
    /// <param name="isSubscription">Whether it is a subscription or a one-time product license</param>
    /// <returns></returns>
    private static async Task<InAppBillingPurchaseOrBillingStatus> GetInAppBillingPurchaseAsync(string productId, bool isSubscription = false)
    {
        Utilities.DebugMsg("In GetInAppBillingPurchaseAsync for " + productId + (isSubscription ? " subscription" : " license"));
        if (Connectivity.NetworkAccess != NetworkAccess.Internet)
        {
            Utilities.DebugMsg("In GetInAppBillingPurchaseAsync, no Internet, returning null");
            return BillingStatusType.noInternet;
        }
        var openRes = await OpenBilling();
        switch (openRes)
        {
            case Billing.BillingStatusType billingStatus:
                Utilities.DebugMsg("In GetInAppBillingPurchaseAsync, OpenBilling returned " + billingStatus);
                return billingStatus;
            case IInAppBilling Interface:
                Utilities.DebugMsg("In GetInAppBillingPurchaseAsync, OpenBilling returned a valid IInAppBilling interface");
                try
                {
                    IEnumerable<InAppBillingPurchase> purchaseList = await Interface.GetPurchasesAsync(isSubscription ? ItemType.Subscription : ItemType.InAppPurchase);

                    InAppBillingPurchase? purchase = purchaseList?.Where(p => p.ProductId == productId).FirstOrDefault();

                    if (purchase is null)
                    {
                        if (purchaseList?.Any() == true)
                            Utilities.DebugMsg($"In GetInAppBillingPurchaseAsync, {productId} not found in play store purchase list, returning notFound");
                        else
                            Utilities.DebugMsg($"In GetInAppBillingPurchaseAsync, {productId} not found, play store purchase list was empty, returning notFound");
                        return BillingStatusType.notFound;
                    }

                    if (!VerifyDivisiBillPurchaseSignature(purchase))
                    {
                        Utilities.DebugMsg($"In GetInAppBillingPurchaseAsync, {purchase.Id} found in play store purchase list but purchase signature was invalid, returning notFound");
                        return BillingStatusType.notFound;
                    }
                    Utilities.DebugMsg($"In GetInAppBillingPurchaseAsync, signed {purchase.Id} found in play store purchase list, verifying with web service");
                    string? validationResult = await CallWs.VerifyPurchase(purchase);

                    if (validationResult is null || !int.TryParse(validationResult, out int scans))
                    {
                        Utilities.DebugMsg($"In GetInAppBillingPurchaseAsync, VerifyPurchase for {purchase.Id} ({purchase.ProductId}) did not return an int, returning failed purchase");
                        purchase.State = PurchaseState.Failed;
                        return purchase;
                    }
                    else if (validationResult == "-408")
                    {
                        Utilities.DebugMsg("In GetInAppBillingPurchaseAsync, VerifyPurchase returned timeout, returning noInternet");
                        return BillingStatusType.noInternet;
                    }

                    purchase.Quantity = scans;

                    Utilities.DebugMsg($"Exiting GetInAppBillingPurchaseAsync, returning {purchase.Id} ({purchase.ProductId}) with {purchase.Quantity} in Quantity field");

                    return purchase;
                }
                catch (InAppBillingPurchaseException pe)
                {
                    Utilities.DebugMsg("In GetInAppBillingPurchaseAsync, billing.VerifyPurchase threw a PurchaseException: " + pe.Message + ", " + pe.PurchaseError.ToString());
                }
                catch (Exception ex)
                {
                    ex.ReportCrash();
                }
                finally
                {
                    await CloseBilling();
                }
                break;
            default:
                Utilities.DebugMsg("In GetInAppBillingPurchaseAsync, OpenBilling returned an unexpected type: " + openRes.GetType().Name);
                return BillingStatusType.connectionFailed;
        }
        Utilities.DebugMsg("Exiting GetInAppBillingPurchaseAsync, returning null");
        return BillingStatusType.notFound;
    }
    #endregion
    #endregion
}
