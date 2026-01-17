namespace DivisiBill.InAppBilling;

/// <summary>
/// Base implementation for In App Billing, handling disposables
/// </summary>

public abstract class BaseInAppBilling : IInAppBilling, IDisposable
{

    /// <summary>
    /// If connected to the store
    /// </summary>
    public virtual bool IsConnected { get; set; } = true;

    /// <summary>
    /// Gets or sets if in testing mode
    /// </summary>
    public abstract bool InTestingMode { get; set; }

    /// <summary>
    /// Connect to billing service
    /// </summary>
    /// <returns>If Success</returns>
    public virtual Task<bool> ConnectAsync(bool enablePendingPurchases = true, CancellationToken cancellationToken = default) => Task.FromResult(true);

    /// <summary>
    /// Disconnect from the billing service
    /// </summary>
    /// <returns>Task to disconnect</returns>
    public virtual Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// Get all current purchases for a specific product type. If verification fails for some purchase, it's not contained in the result.
    /// </summary>
    /// <param name="itemType">Type of product</param>
    /// <returns>The current purchases</returns>
    public abstract Task<IEnumerable<InAppBillingPurchase>> GetPurchasesAsync(ItemType itemType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Purchase a specific product or subscription
    /// </summary>
    /// <param name="productId">Sku or ID of product</param>
    /// <param name="itemType">Type of product being requested</param>
    /// <param name="obfuscatedAccountId">Specifies an optional obfuscated string that is uniquely associated with the user's account in your app.</param>
    /// <param name="obfuscatedProfileId">Specifies an optional obfuscated string that is uniquely associated with the user's profile in your app.</param>
    /// <returns>Purchase details</returns>
    /// <exception cref="InAppBillingPurchaseException">If an error occurs during processing</exception>
    public abstract Task<InAppBillingPurchase> PurchaseAsync(string productId, ItemType itemType, string obfuscatedAccountId = null, string obfuscatedProfileId = null, string subOfferToken = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consume a purchase with a purchase token.
    /// </summary>
    /// <param name="productId">Product Id</param>
    /// <param name="transactionIdentifier">Original Purchase Token</param>
    /// <returns>If consumed successful</returns>
    /// <exception cref="InAppBillingPurchaseException">If an error occurs during processing</exception>
    public abstract Task<bool> ConsumePurchaseAsync(string productId, string transactionIdentifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of class and parent classes
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Dispose up
    /// </summary>
    ~BaseInAppBilling()
    {
        Dispose(false);
    }

    private bool disposed = false;
    /// <summary>
    /// Dispose method
    /// </summary>
    /// <param name="disposing"></param>
    public virtual void Dispose(bool disposing)
    {
        if (!disposed)
        {
            if (disposing)
            {
                //dispose only
            }

            disposed = true;
        }
    }
}
