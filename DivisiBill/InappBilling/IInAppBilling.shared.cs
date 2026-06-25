namespace DivisiBill.InAppBilling;

/// <summary>
/// Interface for InAppBilling
/// </summary>
[Preserve(AllMembers = true)]
public interface IInAppBilling : IDisposable
{
    /// <summary>
    /// Determines if it is connected to the back end actively (Android).
    /// </summary>
    bool IsConnected { get; set; }
    /// <summary>
    /// Gets or sets if in testing mode
    /// </summary>
    bool InTestingMode { get; set; }

    /// <summary>
    /// Connect to billing service
    /// </summary>
    /// <returns>If Success</returns>
    Task<bool> ConnectAsync(bool enablePendingPurchases = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the billing service
    /// </summary>
    /// <returns>Task to disconnect</returns>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all current purchases for a specific product type. If you use verification and it fails for some purchase, it's not contained in the result.
    /// </summary>
    /// <param name="itemType">Type of product</param>
    /// <returns>The current purchases</returns>
    Task<IEnumerable<InAppBillingPurchase>> GetPurchasesAsync(ItemType itemType, CancellationToken cancellationToken = default);


    /// <summary>
    /// Purchase a specific product or subscription
    /// </summary>
    /// <param name="productId">Sku or ID of product</param>
    /// <param name="itemType">Type of product being requested</param>
    /// <param name="obfuscatedAccountId">Android: Specifies an optional obfuscated string that is uniquely associated with the user's account in your app.</param>
    /// <param name="obfuscatedProfileId">Android: Specifies an optional obfuscated string that is uniquely associated with the user's profile in your app.</param>
    /// <returns>Purchase details</returns>
    /// <exception cref="InAppBillingPurchaseException">If an error occurs during processing</exception>
    Task<InAppBillingPurchase?> PurchaseAsync(string productId, ItemType itemType, string? obfuscatedAccountId = null, string? obfuscatedProfileId = null, string? subOfferToken = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consume a purchase with a purchase token.
    /// </summary>
    /// <param name="productId">Product id or sku</param>
    /// <param name="transactionIdentifier">Original Purchase Token</param>
    /// <returns>If consumed successful</returns>
    /// <exception cref="InAppBillingPurchaseException">If an error occurs during processing</exception>
    Task<bool> ConsumePurchaseAsync(string productId, string transactionIdentifier, CancellationToken cancellationToken = default);
}
