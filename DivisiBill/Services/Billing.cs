using Plugin.InAppBilling;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DivisiBill.Services;

/// <summary>
/// <para>The billing class handles matters related to in-app billing. It relies on the InAppBilling plug-in
/// (the Plugin.InAppBilling nuget package) and the DivisiBill web service. For the purposes of this
/// discussion the license may be for a product or a subscription.</para>
/// 
/// <para>Professional licenses (usually subscriptions) enable cloud features, OCR licenses enable scans, 
/// you can buy an OCR license one whenever your scan counts drop below a threshold. Buying one adds a
/// fixed number of scans which then decrement as you perform OCR scans on individual bills. When the scan count
/// reaches zero we notify the store that the license has been consumed and you must buy another before more 
/// scans are allowed. The tracking is mostly done by the web service, but we keep a local copy of how many scans
/// we think are left for convenience even though the web service value is definitive.</para>
/// 
/// <para>The flow is that a user buys a license through the program using the store for the platform (only Android at
/// present) and then presents it to a web service for validation. The web service ALSO calls the store to validate the
/// license it was given was issued by the store and also checks that it is not in its list of known licenses. if that
/// validation passes, the license is stored in a table (so it's now a known one) and a value is returned to the caller
/// to tell it to acknowledge the license with the store.
/// If it is an OCR license we also return a count of OCR scans it enables the user to consume, and persist the new total
/// including unused scans from a previous license.</para>
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
    /// <summary>
    /// The status of a billing operation, typically returned from a method that performs some billing operation.
    /// </summary>
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

    private static string GetJsonFieldValue(string jsonString, string fieldName) => JsonDocument.Parse(jsonString).RootElement.TryGetProperty(fieldName, out JsonElement fieldValue) ? fieldValue.GetString() : string.Empty;

    #region Pro License
    public const string ProSubscriptionId = "pro.subscription";
    public const string OldProProductId = "pro.upgrade"; // a product, not a subscription, kept around to simplify testing because it does not expire
    internal static InAppBillingPurchase ProPurchase { get; private set; } = null;
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
#if DEBUG
        if (DeviceInfo.Platform == DevicePlatform.WinUI || (DeviceInfo.Platform == DevicePlatform.Android && "Subsystem for Android(TM)".Equals(DeviceInfo.Model)))
        {
            if (string.IsNullOrWhiteSpace(DivisiBill.Generated.BuildInfo.DivisiBillTestProJsonB64))
            {
                Utilities.DebugMsg("In GetHasProSubscriptionAsync, DivisiBillTestProJsonB64 was empty");
                ProPurchase = new InAppBillingPurchase() { State = PurchaseState.Failed };
                return BillingStatusType.notLicensing; // a specific error so it can be handled silently 
            }
            else
            {
                string json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(Generated.BuildInfo.DivisiBillTestProJsonB64));
                string resultString = await GetInAppBillingPurchaseFakeAsync(json);
                ProPurchase = new InAppBillingPurchase()
                {
                    ProductId = OldProProductId, // temporary
                    State = PurchaseState.Failed,
                    Id = GetJsonFieldValue(json, "orderId"),
                    OriginalJson = json
                };
                if (resultString is not null)
                {
                    ProPurchase.State = PurchaseState.Purchased;
                    HasOldProProductId = true;
                    return BillingStatusType.ok; // No error
                }
            }
        }
        else
#endif
        if (DeviceInfo.Current.Platform == DevicePlatform.Android)
        {
            Billing.BillingStatusType billingResultOld = BillingStatusType.notFound;
            Billing.BillingStatusType billingResult = BillingStatusType.notFound;
            try
            {
                #region Old Style Pro Product (used for Testing)
                Utilities.DebugMsg("In GetHasProSubscriptionAsync, trying old style pro product");
                (billingResultOld, ProPurchase) = await GetInAppBillingPurchaseAsync(OldProProductId, isSubscription: false);
                if (billingResultOld == BillingStatusType.ok && ProPurchase is not null && ProPurchase.State == PurchaseState.Purchased)
                {
                    Utilities.DebugMsg("Exiting GetHasProSubscriptionAsync, found old style pro product " + ProPurchase.Id);
                    HasOldProProductId = true;
                    return BillingStatusType.ok; // No error
                }
                else if (billingResultOld >= BillingStatusType.noInternet)
                    return billingResultOld;
                Utilities.DebugMsg("In GetHasProSubscriptionAsync, did not find old style pro product");
                #endregion
                #region New Style Pro Subscription
                Utilities.DebugMsg("In GetHasProSubscriptionAsync, awaiting GetInAppBillingPurchaseAsync(ProSubscriptionId)");
                (billingResult, ProPurchase) = await GetInAppBillingPurchaseAsync(ProSubscriptionId, isSubscription: true);
                if (billingResult == BillingStatusType.ok && ProPurchase is not null && ProPurchase.State == PurchaseState.Purchased)
                {
                    Utilities.DebugMsg("Exiting GetHasProSubscriptionAsync, found proPurchase " + ProPurchase.Id);
                    return BillingStatusType.ok; // No error
                }
                #endregion
                Utilities.DebugMsg("In GetHasProSubscriptionAsync, did not find new style pro subscription");
            }
            catch (Exception ex)
            {
                Utilities.DebugMsg("In GetHasProSubscriptionAsync, threw an exception:" + ex);
            }
            // If the old style license was not found (the normal case) return the status of the subscription
            return billingResultOld == BillingStatusType.notFound ? billingResult : billingResultOld;
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
        if (ProPurchase is null)
            Utilities.DebugMsg("In Billing.PurchaseProSubscriptionAsync, PurchaseItemAsync returned null");
        else
        {
            string validationResult = await CallWs.VerifyPurchase(ProPurchase, isSubscription: true);
            if (validationResult is null)
                Utilities.DebugMsg("In Billing.PurchaseProSubscriptionAsync, CallWs.VerifyPurchase returned null");
            else
                return true;
        }
        Utilities.DebugMsg("Returning FALSE from Billing.PurchaseProSubscriptionAsync");
        return false;
    }
    #endregion
    #region OCR License

    public static readonly string OcrLicenseProductId = "ocr.calls";
    internal static int ScansLeft { get; set; }
    internal static InAppBillingPurchase OcrPurchase { get; private set; } = null;
    /// <summary>
    /// Check whether the user has an OCR license, if it is valid, and if it has scans left
    /// </summary>
    /// <returns>Scans remaining or a negative number if the license was invalid</returns>
    internal static async Task<int> GetHasOcrLicenseAsync()
    {
#if DEBUG
        if (DeviceInfo.Platform == DevicePlatform.WinUI || (DeviceInfo.Platform == DevicePlatform.Android && "Subsystem for Android(TM)".Equals(DeviceInfo.Model)))
        {
            if (string.IsNullOrWhiteSpace(DivisiBill.Generated.BuildInfo.DivisiBillTestOcrJsonB64))
            {
                Utilities.DebugMsg("In GetHasProSubscriptionAsync, DivisiBillTestProJsonB64 was empty");
                OcrPurchase = new InAppBillingPurchase() { State = PurchaseState.Failed };
                return -1; // error 
            }
            else
            {
                string json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(Generated.BuildInfo.DivisiBillTestOcrJsonB64));
                string resultString = await GetInAppBillingPurchaseFakeAsync(json);
                OcrPurchase = new InAppBillingPurchase() // Set regardless of whether verification works or fails
                {
                    ProductId = OcrLicenseProductId,
                    State = PurchaseState.Failed,
                    Id = GetJsonFieldValue(json, "orderId"),
                    OriginalJson = json
                };
                if (resultString is not null && int.TryParse(resultString, out int scans))
                {
                    OcrPurchase.State = PurchaseState.Purchased;
                    ScansLeft = scans;
                    return scans;
                }
            }
        }
        else
#endif
        if (DeviceInfo.Platform == DevicePlatform.Android)
        {
            Utilities.DebugMsg($"In GetHasOcrLicenseAsync, awaiting GetInAppBillingPurchaseAsync(\"{OcrLicenseProductId}\")");
            Billing.BillingStatusType billingResult = BillingStatusType.notFound;
            (billingResult, OcrPurchase) = await GetInAppBillingPurchaseAsync(OcrLicenseProductId);
            if (billingResult == BillingStatusType.ok && OcrPurchase is not null && OcrPurchase.State == PurchaseState.Purchased)
            {
                ScansLeft = OcrPurchase.Quantity;
                Utilities.DebugMsg("Exiting GetHasOcrLicenseAsync, returning " + ScansLeft);
                return ScansLeft;
            }
            Utilities.DebugMsg("Exiting GetHasOcrLicenseAsync in ERROR, billingResult = " + billingResult);
            ScansLeft = 0;
        }
        return -1;
    }
    /// <summary>
    /// Purchase an OCR license from an app store then check it against our web service to make sure it is legitimate.
    /// </summary>
    /// <returns>Scans remaining or a negative number if the purchase failed</returns>
    internal static async Task<int> PurchaseOcrLicenseAsync()
    {
        Debug.Assert(App.Settings is not null);
        if (await Billing.GetHasOcrLicenseAsync() < Billing.ScansWarningLevel)
            await Billing.ConsumeDepletedOcrLicense();
        OcrPurchase = await PurchaseItemAsync(OcrLicenseProductId, App.Settings.UserKey);
        if (OcrPurchase is null) return -1;
        string validationResult = await CallWs.VerifyPurchase(OcrPurchase, isSubscription: false);
        if (validationResult is null || !int.TryParse(validationResult, out int ocrLicenseScans))
            return -2;
        ScansLeft = ocrLicenseScans;
        Utilities.DebugMsg($"In PurchaseOcrLicenseAsync, OCR scans purchased = {ocrLicenseScans}, scans left = {ScansLeft}");
        return ocrLicenseScans;
    }
    /// <summary>
    /// Remove an OCR license from the store (but not from our list of used licenses) once it has no scans attached any more
    /// </summary>
    internal static async Task ConsumeDepletedOcrLicense()
    {
        if (Utilities.IsUWP)
            return; // Not implemented for Windows 
        int purchaseCount = await GetHasOcrLicenseAsync();
        Utilities.DebugMsg("In ConsumeDepletedOcrLicense, license purchase test returned " + purchaseCount);
        if (purchaseCount >= ScansWarningLevel) // Too many scans associated with this license have not yet been consumed
            Utilities.DebugMsg($"In ConsumeDepletedOcrLicense, because license still has {purchaseCount} scans it will not be removed");
        else if (OcrPurchase is not null)
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
    private static async Task<(BillingStatusType Status, IInAppBilling Interface)> OpenBilling([CallerMemberName] string methodName = "UnknownMethod")
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
                return (BillingStatusType.connectionFaulted, null);
            }
        }
        if (CrossInAppBilling.Current.IsConnected)
        {
            Utilities.DebugMsg("In OpenBilling: Connected to CrossInAppBilling");
            BillingConnections++;
            return (BillingStatusType.ok, CrossInAppBilling.Current);
        }
        else
        {
            Utilities.DebugMsg("In OpenBilling: Could not connect to CrossInAppBilling, returning null");
            return (BillingStatusType.connectionFailed, null);
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

    /// <summary>
    /// Purchase either a product or a subscription from the app store (just the Google play Store for now).
    /// </summary>
    /// <param name="productId">The ID of the product or subscription to be purchased</param>
    /// <param name="isSubscription">Whether it is a subscription (true) or a product (false)</param>
    /// <returns></returns>
    private static async Task<InAppBillingPurchase> PurchaseItemAsync(string productId, string obfuscatedAccountId, bool isSubscription = false)
    {
        if (Connectivity.NetworkAccess != NetworkAccess.Internet)
        {
            // No Internet, don't even bother trying
            return null;
        }
        InAppBillingPurchase purchase = null;
        (_, var inAppBilling) = await OpenBilling();
        if (inAppBilling is null)
        {
            //we are off line or can't connect, don't try to purchase
            return null;
        }
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
            {
                return null;
            }
            else if (purchase.State == PurchaseState.Purchased)
            {
                // So the purchase record looks good, now call our web service to record it and make sure it's not being reused
                bool recorded = await CallWs.RecordPurchaseAsync(purchase, isSubscription);

                if (recorded)
                {
                    purchase.IsAcknowledged = true; // Because the web service acknowledges the purchase 
                    return purchase;
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
        Utilities.DebugMsg("In Billing.PurchaseItemAsync:  Attempt to record license failed");
        return null;
    }

    /// <summary>
    /// Consume a license that has been used up, so the user can buy another one, used with OCR licenses.
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
        var (_, Interface) = await OpenBilling();
        if (Interface is null)
        {
            //we are off line or can't connect, don't try to do anything
            return false;
        }
        try
        {
            var consumedItem = await Interface.ConsumePurchaseAsync(productId, purchaseToken);

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
    }

#if DEBUG
    /// <summary>
    /// A fake version of GetInAppBillingPurchaseAsync that uses a pre-formatted JSON string to simulate a purchase.
    /// Only compiled in DEBUG builds and only used on Windows for testing.
    /// </summary>
    /// <param name="androidJson">JSON representation of a license</param>
    /// <returns></returns>
    private static async Task<string> GetInAppBillingPurchaseFakeAsync(string androidJson)
    {
        Utilities.DebugMsg("In GetInAppBillingPurchaseFakeAsync");
        JsonNode androidJsonObject = JsonNode.Parse(androidJson);
        if (androidJsonObject is null)
        {
            Utilities.DebugMsg("In GetInAppBillingPurchaseFakeAsync, androidJsonObject was null, returning null");
            return null;
        }
        JsonNode productIdNode = androidJsonObject["productId"];
        if (productIdNode is null || productIdNode.GetValue<string>() is not string productId)
        {
            Utilities.DebugMsg("In GetInAppBillingPurchaseFakeAsync, productIdNode was not a string, returning null");
            return null;
        }

        if (Connectivity.NetworkAccess != NetworkAccess.Internet)
        {
            Utilities.DebugMsg("In GetInAppBillingPurchaseFakeAsync, no Internet, returning null");
            return null;
        }
        try
        {
            string validationResult = await CallWs.VerifyAndroidPurchase(androidJson, productId, isSubscription: false);

            if (validationResult is null)
            {
                Utilities.DebugMsg("In GetInAppBillingPurchaseFakeAsync, VerifyPurchase returned null, returning null");
                return null;
            }

            Utilities.DebugMsg($"Exiting GetInAppBillingPurchaseFakeAsync, returning '{validationResult}'");

            return validationResult;
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
        }
        return null;
    }
#endif

    /// <summary>
    /// Get a license object for a named product by checking the store for a record of it, then verifying the returned license with the web service.
    /// </summary>
    /// <param name="productId">The product Id we need a license for</param>
    /// <param name="isSubscription">Whether it is a subscription or a one-time product</param>
    /// <returns></returns>
    private static async Task<(BillingStatusType, InAppBillingPurchase)> GetInAppBillingPurchaseAsync(string productId, bool isSubscription = false)
    {
        Utilities.DebugMsg("In GetInAppBillingPurchaseAsync for " + productId + (isSubscription ? " subscription" : " license"));
        if (Connectivity.NetworkAccess != NetworkAccess.Internet)
        {
            Utilities.DebugMsg("In GetInAppBillingPurchaseAsync, no Internet, returning null");
            return (BillingStatusType.noInternet, null);
        }
        var (Status, Interface) = await OpenBilling();
        if (Interface == null)
        {
            Utilities.DebugMsg("In GetInAppBillingPurchaseAsync, no billing connection, returning null");
            return (Status, null);
        }
        try
        {
            var purchaseList = await Interface.GetPurchasesAsync(isSubscription ? ItemType.Subscription : ItemType.InAppPurchase);

            var purchase = purchaseList?.Where(p => p.ProductId == productId).FirstOrDefault();

            if (purchase is null)
            {
                if (purchaseList.Any())
                    Utilities.DebugMsg($"In GetInAppBillingPurchaseAsync, {productId} not found in play store purchase list, returning null");
                else
                    Utilities.DebugMsg($"In GetInAppBillingPurchaseAsync, {productId} not found, play store purchase list was empty, returning null");
                return (BillingStatusType.notFound, null);
            }
            else
                Utilities.DebugMsg($"In GetInAppBillingPurchaseAsync, {productId} found in play store purchase list, verifying with web service");


            string validationResult = await CallWs.VerifyPurchase(purchase, isSubscription);

            if (validationResult is null || !int.TryParse(validationResult, out int scans))
            {
                Utilities.DebugMsg("In GetInAppBillingPurchaseAsync, VerifyPurchase did not return an int, returning failed purchase");
                purchase.State = PurchaseState.Failed;
                return (BillingStatusType.notVerified, purchase);
            }

            purchase.Quantity = scans;

            Utilities.DebugMsg("Exiting GetInAppBillingPurchaseAsync, returning purchase record with scans in Quantity field");

            return (BillingStatusType.ok, purchase);
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
        Utilities.DebugMsg("Exiting GetInAppBillingPurchaseAsync, returning null");
        return (BillingStatusType.notFound, null);
    }
    #endregion
    #region  Pre-formatted android license Json for use in testing 
#if DEBUG // omit thee secrets from production code
    private const string fakeProJson = // Used to test the web service without connecting to Google
        $$"""
        {
            "orderId": "Fake-OrderId",
            "packageName": "com.autoplus.divisibill",
            "productId": "pro.upgrade",
            "purchaseTime": 1669436566877,
            "purchaseToken": "Fake purchase token",
            "purchaseState": 1,
            "quantity": 1,
            "acknowledgementState": 1,
            "acknowledged": true
        }
        """;
#endif
    #endregion
}
