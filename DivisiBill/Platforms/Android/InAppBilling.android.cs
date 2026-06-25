using Android.App;
using Android.BillingClient.Api;
using Android.Content;
using DivisiBill.InAppBilling;
using static Android.BillingClient.Api.BillingClient;
using BillingResponseCode = Android.BillingClient.Api.BillingResponseCode;

namespace DivisiBill.Platforms.Android;

/// <summary>
/// Implementation for Feature
/// </summary>
[Preserve(AllMembers = true)]
public class InAppBillingImplementation : BaseInAppBilling
{
    /// <summary>
    /// Gets or sets a callback for out of band purchases to complete.
    /// </summary>
    public static Action<BillingResult, List<InAppBillingPurchase>>? OnAndroidPurchasesUpdated { get; set; } = null;

    /// <summary>
    /// Gets the context, aka the currently activity.
    /// This is set from the MainApplication.cs file that was laid down by the plugin
    /// </summary>
    /// <value>The context.</value>
    private static Activity Activity =>
        Platform.CurrentActivity ?? throw new NullReferenceException("Current Activity is null, ensure that the MainActivity.cs file is configuring .NET MAUI in your source code so the In App Billing can use it.");

    private static Context Context => global::Android.App.Application.Context;

    /// <summary>
    /// Default Constructor for In App Billing Implementation on Android
    /// </summary>
    public InAppBillingImplementation()
    {

    }

    private BillingClient? BillingClient { get; set; }
    private BillingClient.Builder? BillingClientBuilder { get; set; }
    /// <summary>
    /// Determines if it is connected to the backend actively (Android).
    /// </summary>
    public override bool IsConnected { get; set; }
    private TaskCompletionSource<(BillingResult billingResult, IList<Purchase> purchases)>? tcsPurchase;
    private TaskCompletionSource<bool>? tcsConnect;
    /// <summary>
    /// Connect to billing service
    /// </summary>
    /// <returns>If Success</returns>
    public override Task<bool> ConnectAsync(bool enablePendingPurchases = true, CancellationToken cancellationToken = default)
    {
        tcsPurchase?.TrySetCanceled();
        tcsPurchase = null;

        tcsConnect?.TrySetCanceled();
        tcsConnect = new TaskCompletionSource<bool>();

        using CancellationTokenRegistration _ = cancellationToken.Register(() => tcsConnect?.TrySetCanceled());
        BillingClientBuilder = NewBuilder(Context);
        BillingClientBuilder.SetListener(OnPurchasesUpdated);
        if (enablePendingPurchases)
        {
            PendingPurchasesParams pendingParams = PendingPurchasesParams.NewBuilder().EnableOneTimeProducts().EnablePrepaidPlans().Build();
            BillingClient = BillingClientBuilder.EnablePendingPurchases(pendingParams).Build();
        }
        else

        {
            PendingPurchasesParams pendingParams = PendingPurchasesParams.NewBuilder().EnableOneTimeProducts().Build();
            BillingClient = BillingClientBuilder.EnablePendingPurchases(pendingParams).Build();
        }

        BillingClient.StartConnection(OnSetupFinished, OnDisconnected);
        // TODO: stop trying

        return tcsConnect.Task;

        void OnSetupFinished(BillingResult billingResult)
        {
            Console.WriteLine($"Billing Setup Finished : {billingResult.ResponseCode} - {billingResult.DebugMessage}");
            IsConnected = billingResult.ResponseCode == BillingResponseCode.Ok;
            tcsConnect?.TrySetResult(IsConnected);
        }

        void OnDisconnected() => IsConnected = false;
    }

    public void OnPurchasesUpdated(BillingResult billingResult, IList<global::Android.BillingClient.Api.Purchase> purchases)
    {
        tcsPurchase?.TrySetResult((billingResult, purchases));

        if (OnAndroidPurchasesUpdated == null || purchases is null || purchases.Count == 0)
            return;

        OnAndroidPurchasesUpdated.Invoke(billingResult, [.. purchases.Select(p => p.ToIABPurchase())]);
    }

    /// <summary>
    /// Disconnect from the billing service
    /// </summary>
    /// <returns>Task to disconnect</returns>
    public override Task DisconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            BillingClientBuilder?.Dispose();
            BillingClientBuilder = null;
            BillingClient?.EndConnection();
            BillingClient?.Dispose();
            BillingClient = null;
            IsConnected = false;
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to disconnect: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets or sets if in testing mode. Only for UWP
    /// </summary>
    public override bool InTestingMode { get; set; }

    public override async Task<IEnumerable<InAppBillingPurchase>> GetPurchasesAsync(ItemType itemType, CancellationToken cancellationToken)
    {
        if (BillingClient == null)
            throw new InAppBillingPurchaseException(PurchaseError.ServiceUnavailable, "You are not connected to the Google Play App store.");

        string skuType = itemType switch
        {
            ItemType.InAppPurchase => ProductType.Inapp,
            ItemType.InAppPurchaseConsumable => ProductType.Inapp,
            _ => ProductType.Subs
        };

        QueryPurchasesParams query = QueryPurchasesParams.NewBuilder().SetProductType(skuType).Build();
        QueryPurchasesResult purchasesResult = await BillingClient.QueryPurchasesAsync(query);

        ParseBillingResult(purchasesResult.Result);

        return purchasesResult.Purchases?.Select(p => p.ToIABPurchase()) ?? Enumerable.Empty<InAppBillingPurchase>();
    }

    /// <summary>
    /// Purchase a specific product or subscription
    /// </summary>
    /// <param name="productId">Sku or ID of product</param>
    /// <param name="itemType">Type of product being requested</param>
    /// <param name="obfuscatedAccountId">Specifies an optional obfuscated string that is uniquely associated with the user's account in your app.</param>
    /// <param name="obfuscatedProfileId">Specifies an optional obfuscated string that is uniquely associated with the user's profile in your app.</param>
    /// <returns></returns>
    public override async Task<InAppBillingPurchase?> PurchaseAsync(string productId, ItemType itemType, string? obfuscatedAccountId = null, string? obfuscatedProfileId = null, string? subOfferToken = null, CancellationToken cancellationToken = default)
    {
        if (BillingClient == null || !IsConnected)
        {
            throw new InAppBillingPurchaseException(PurchaseError.ServiceUnavailable, "You are not connected to the Google Play App store.");
        }

        // If we have a current task and it is not completed then return null.
        // you can't try to purchase twice.
        //AssertPurchaseTransactionReady();

        if (tcsPurchase?.Task != null && !tcsPurchase.Task.IsCompleted)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(obfuscatedProfileId) && string.IsNullOrWhiteSpace(obfuscatedAccountId))
            throw new ArgumentNullException(nameof(obfuscatedAccountId), "You must set an account id if you are setting a profile id");

        switch (itemType)
        {
            case ItemType.InAppPurchase:
            case ItemType.InAppPurchaseConsumable:
                return await PurchaseAsync(productId, ProductType.Inapp, obfuscatedAccountId, obfuscatedProfileId, null, cancellationToken);
            case ItemType.Subscription:

                BillingResult result = BillingClient.IsFeatureSupported(FeatureType.Subscriptions);
                ParseBillingResult(result);
                return await PurchaseAsync(productId, ProductType.Subs, obfuscatedAccountId, obfuscatedProfileId, subOfferToken, cancellationToken);
        }

        return null;
    }

    private async Task<InAppBillingPurchase?> PurchaseAsync(string productSku, string itemType, string? obfuscatedAccountId = null, string? obfuscatedProfileId = null, string? subOfferToken = null, CancellationToken cancellationToken = default)
    {

        QueryProductDetailsParams.Product productList = QueryProductDetailsParams.Product.NewBuilder()
            .SetProductType(itemType)
            .SetProductId(productSku)
            .Build();

        QueryProductDetailsParams.Builder skuDetailsParams = QueryProductDetailsParams.NewBuilder().SetProductList(new[] { productList });

        QueryProductDetailsResult skuDetailsResult = await BillingClient!.QueryProductDetailsAsync(skuDetailsParams.Build());

        //ParseBillingResult(skuDetailsResult.Result);

        ProductDetails skuDetails = skuDetailsResult.ProductDetailsList.FirstOrDefault() ?? throw new ArgumentException($"{productSku} does not exist");
        BillingFlowParams.ProductDetailsParams productDetailsParamsList;

        if (itemType == ProductType.Subs)
        {
            string t = subOfferToken ?? skuDetails.GetSubscriptionOfferDetails()?.FirstOrDefault()?.OfferToken ?? string.Empty;

            BillingFlowParams.ProductDetailsParams.Builder productDetails = BillingFlowParams.ProductDetailsParams.NewBuilder().SetProductDetails(skuDetails);

            productDetailsParamsList = string.IsNullOrWhiteSpace(t) ? productDetails.Build() : productDetails.SetOfferToken(t).Build();
        }
        else
        {
            productDetailsParamsList = BillingFlowParams.ProductDetailsParams.NewBuilder()
            .SetProductDetails(skuDetails)
            .Build();
        }

        BillingFlowParams.Builder billingFlowParams = BillingFlowParams.NewBuilder()
            .SetProductDetailsParamsList(new[] { productDetailsParamsList });

        if (!string.IsNullOrWhiteSpace(obfuscatedAccountId))
            billingFlowParams.SetObfuscatedAccountId(obfuscatedAccountId);

        if (!string.IsNullOrWhiteSpace(obfuscatedProfileId))
            billingFlowParams.SetObfuscatedProfileId(obfuscatedProfileId);

        BillingFlowParams flowParams = billingFlowParams.Build();

        tcsPurchase = new TaskCompletionSource<(BillingResult billingResult, IList<Purchase> purchases)>();
        CancellationTokenRegistration _ = cancellationToken.Register(() => tcsPurchase!.TrySetCanceled());

        BillingResult responseCode = BillingClient!.LaunchBillingFlow(Activity, flowParams);

        ParseBillingResult(responseCode);

        (BillingResult billingResult, IList<Purchase> purchases) result = await tcsPurchase!.Task;
        ParseBillingResult(result.billingResult);

        //we are only buying 1 thing.
        Purchase? androidPurchase = result.purchases?.FirstOrDefault(p => p.Products.Contains(productSku));

        //for some reason the data didn't come back
        if (androidPurchase is null)
        {
            IEnumerable<InAppBillingPurchase> purchases = await GetPurchasesAsync(itemType == ProductType.Inapp ? ItemType.InAppPurchase : ItemType.Subscription, cancellationToken);
            return purchases.FirstOrDefault(p => productSku.Equals(p.ProductId, StringComparison.OrdinalIgnoreCase));
        }

        return androidPurchase.ToIABPurchase();
    }


    /// <summary>
    /// Consume a purchase with a purchase token.
    /// in app:{Context.PackageName}:{productSku}
    /// </summary>
    /// <param name="productId">Id or Sku of product</param>
    /// <param name="transactionIdentifier">Original Purchase Token</param>
    /// <returns>If consumed successful</returns>
    public override async Task<bool> ConsumePurchaseAsync(string productId, string transactionIdentifier, CancellationToken cancellationToken)
    {
        if (BillingClient == null || !IsConnected)
        {
            throw new InAppBillingPurchaseException(PurchaseError.ServiceUnavailable, "You are not connected to the Google Play App store.");
        }


        ConsumeParams consumeParams = ConsumeParams.NewBuilder()
            .SetPurchaseToken(transactionIdentifier)
            .Build();

        ConsumeResult result = await BillingClient.ConsumeAsync(consumeParams);


        return ParseBillingResult(result.BillingResult);
    }

    private static bool ParseBillingResult(BillingResult result, bool ignoreInvalidProducts = false)
    {
        if (result == null)
            throw new InAppBillingPurchaseException(PurchaseError.GeneralError);

        if (result.ResponseCode == BillingResponseCode.NetworkError)
            throw new InAppBillingPurchaseException(PurchaseError.ServiceTimeout);//Network connection is down

        return result.ResponseCode switch
        {
            BillingResponseCode.Ok => true,
            BillingResponseCode.NetworkError => throw new InAppBillingPurchaseException(PurchaseError.NetworkError),
            BillingResponseCode.UserCancelled => throw new InAppBillingPurchaseException(PurchaseError.UserCancelled),//User Cancelled, should try again
            BillingResponseCode.ServiceUnavailable => throw new InAppBillingPurchaseException(PurchaseError.ServiceUnavailable),//Network connection is down
            BillingResponseCode.ServiceDisconnected => throw new InAppBillingPurchaseException(PurchaseError.ServiceDisconnected),//Network connection is down
            BillingResponseCode.ServiceTimeout => throw new InAppBillingPurchaseException(PurchaseError.ServiceTimeout),//Network connection is down
            BillingResponseCode.BillingUnavailable => throw new InAppBillingPurchaseException(PurchaseError.BillingUnavailable),//Billing Unavailable
            BillingResponseCode.ItemNotOwned => throw new InAppBillingPurchaseException(PurchaseError.NotOwned),//Item not owned
            BillingResponseCode.DeveloperError => throw new InAppBillingPurchaseException(PurchaseError.DeveloperError),//Developer Error
            BillingResponseCode.Error => throw new InAppBillingPurchaseException(PurchaseError.GeneralError),//Generic Error
            BillingResponseCode.FeatureNotSupported => throw new InAppBillingPurchaseException(PurchaseError.FeatureNotSupported),
            BillingResponseCode.ItemAlreadyOwned => throw new InAppBillingPurchaseException(PurchaseError.AlreadyOwned),
            BillingResponseCode.ItemUnavailable => ignoreInvalidProducts ? false : throw new InAppBillingPurchaseException(PurchaseError.ItemUnavailable),
            _ => false,
        };
    }
}
