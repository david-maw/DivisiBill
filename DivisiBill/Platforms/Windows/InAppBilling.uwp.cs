using System.Xml;
using Windows.ApplicationModel.Store;
using DivisiBill.InAppBilling;

namespace DivisiBill.Platforms.Windows;

/// <summary>
/// Implementation for Feature
/// </summary>
public class InAppBillingImplementation : BaseInAppBilling
{
    /// <summary>
    /// Default constructor
    /// </summary>
    public InAppBillingImplementation()
    {
    }

    /// <summary>
    /// Gets or sets if in testing mode. Only for UWP
    /// </summary>
    public override bool InTestingMode { get; set; }

    /// <summary>
    /// Get all purchases
    /// </summary>
    /// <param name="itemType"></param>
    /// <returns></returns>
    public override async Task<IEnumerable<InAppBillingPurchase>> GetPurchasesAsync(ItemType itemType, CancellationToken cancellationToken = default)
    {
        // Get list of product receipts from store or simulator
        string xmlReceipt = await CurrentAppMock.GetAppReceiptAsync(InTestingMode);

        // Transform it to list of InAppBillingPurchase
        return xmlReceipt.ToInAppBillingPurchase(ProductPurchaseStatus.AlreadyPurchased);
    }

    /// <summary>
    /// Purchase a specific product or subscription
    /// </summary>
    /// <param name="productId">Sku or ID of product</param>
    /// <param name="itemType">Type of product being requested</param>
    /// <param name="obfuscatedAccountId">Specifies an optional obfuscated string that is uniquely associated with the user's account in your app.</param>
    /// <param name="obfuscatedProfileId">Specifies an optional obfuscated string that is uniquely associated with the user's profile in your app.</param>
    /// <returns></returns>
    /// <exception cref="InAppBillingPurchaseException">If an error occurs during processing</exception>
    public override async Task<InAppBillingPurchase> PurchaseAsync(string productId, ItemType itemType, string obfuscatedAccountId = null, string obfuscatedProfileId = null, string subOfferToken = null, CancellationToken cancellationToken = default)
    {
        // Get purchase result from store or simulator
        PurchaseResults purchaseResult = await CurrentAppMock.RequestProductPurchaseAsync(InTestingMode, productId);


        if (purchaseResult == null)
            return null;

        if (string.IsNullOrWhiteSpace(purchaseResult.ReceiptXml))
            return null;

        // Transform it to InAppBillingPurchase
        return purchaseResult.ReceiptXml.ToInAppBillingPurchase(purchaseResult.Status).FirstOrDefault();

    }

    /// <summary>
    /// Consume a purchase with a purchase token.
    /// </summary>
    /// <param name="productId">Id or Sku of product</param>
    /// <param name="transactionIdentifier">Original Purchase Token</param>
    /// <returns>If consumed successful</returns>
    /// <exception cref="InAppBillingPurchaseException">If an error occurs during processing</exception>
    public override async Task<bool> ConsumePurchaseAsync(string productId, string transactionIdentifier, CancellationToken cancellationToken = default)
    {
        FulfillmentResult result = await CurrentAppMock.ReportConsumableFulfillmentAsync(InTestingMode, productId, new Guid(transactionIdentifier));
        return result switch
        {
            FulfillmentResult.ServerError => throw new InAppBillingPurchaseException(PurchaseError.AppStoreUnavailable),
            FulfillmentResult.NothingToFulfill => throw new InAppBillingPurchaseException(PurchaseError.ItemUnavailable),
            FulfillmentResult.PurchasePending or FulfillmentResult.PurchaseReverted => throw new InAppBillingPurchaseException(PurchaseError.GeneralError),
            FulfillmentResult.Succeeded => true,
            _ => false,
        };
    }
}

/// <summary>
/// Unfortunately, CurrentApp and CurrentAppSimulator do not share an interface or base class
/// This is why, we use a mocking class here
/// </summary>
internal static class CurrentAppMock
{
    public static async Task<IEnumerable<UnfulfilledConsumable>> GetAvailableConsumables(bool isTestingMode) => isTestingMode ? await CurrentAppSimulator.GetUnfulfilledConsumablesAsync() : await CurrentApp.GetUnfulfilledConsumablesAsync();

    public static async Task<FulfillmentResult> ReportConsumableFulfillmentAsync(bool isTestingMode, string productId, Guid transactionId) => isTestingMode ? await CurrentAppSimulator.ReportConsumableFulfillmentAsync(productId, transactionId) : await CurrentApp.ReportConsumableFulfillmentAsync(productId, transactionId);

    public static async Task<ListingInformation> LoadListingInformationAsync(bool isTestingMode) => isTestingMode ? await CurrentAppSimulator.LoadListingInformationAsync() : await CurrentApp.LoadListingInformationAsync();

    public static async Task<string> GetAppReceiptAsync(bool isTestingMode) => isTestingMode ? await CurrentAppSimulator.GetAppReceiptAsync() : await CurrentApp.GetAppReceiptAsync();

    public static async Task<PurchaseResults> RequestProductPurchaseAsync(bool isTestingMode, string productId) => isTestingMode ? await CurrentAppSimulator.RequestProductPurchaseAsync(productId) : await CurrentApp.RequestProductPurchaseAsync(productId);
}

internal static class InAppBillingHelperUwp
{
    /// <summary>
    /// Read purchase data out of the UWP Receipt XML
    /// </summary>
    /// <param name="xml">Receipt XML</param>
    /// <param name="status">Status of the purchase</param>
    /// <returns>A list of purchases, the user has done</returns>
    public static IEnumerable<InAppBillingPurchase> ToInAppBillingPurchase(this string xml, ProductPurchaseStatus status)
    {
        List<InAppBillingPurchase> purchases = [];

        XmlDocument xmlDoc = new();
        try
        {
            xmlDoc.LoadXml(xml);
        }
        catch
        {
            //Invalid XML, we haven't finished this transaction yet.
        }

        // Iterate through all ProductReceipt elements
        XmlNodeList xmlProductReceipts = xmlDoc.GetElementsByTagName("ProductReceipt");
        for (int i = 0; i < xmlProductReceipts.Count; i++)
        {
            XmlNode xmlProductReceipt = xmlProductReceipts[i];

            // Create new InAppBillingPurchase with values from the xml element
            InAppBillingPurchase purchase = new()
            {
                Id = xmlProductReceipt.Attributes["Id"].Value,
                TransactionDateUtc = Convert.ToDateTime(xmlProductReceipt.Attributes["PurchaseDate"].Value),
                ProductId = xmlProductReceipt.Attributes["ProductId"].Value,
                AutoRenewing = false // Not supported by UWP yet
            };
            purchase.PurchaseToken = purchase.Id;
            purchase.TransactionIdentifier = purchase.Id;
            purchase.ProductIds = new string[] { purchase.ProductId };

            // Map native UWP status to PurchaseState
            purchase.State = status switch
            {
                ProductPurchaseStatus.AlreadyPurchased or ProductPurchaseStatus.Succeeded => PurchaseState.Purchased,
                ProductPurchaseStatus.NotFulfilled => PurchaseState.Deferred,
                ProductPurchaseStatus.NotPurchased => PurchaseState.Canceled,
                _ => PurchaseState.Unknown,
            };

            // Add to list of purchases
            purchases.Add(purchase);
        }

        return purchases;
    }
}
