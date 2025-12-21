using Android.BillingClient.Api;

namespace DivisiBill.InAppBilling
{
    internal static class Converters
    {
        internal static InAppBillingPurchase ToIABPurchase(this Purchase purchase)
        {
            var finalPurchase = new InAppBillingPurchase
            {
                AutoRenewing = purchase.IsAutoRenewing,
                Id = purchase.OrderId,
                OriginalJson = purchase.OriginalJson,
                Signature = purchase.Signature,
                IsAcknowledged = purchase.IsAcknowledged,
                Payload = purchase.DeveloperPayload,
                ProductId = purchase.Products?.FirstOrDefault(),
                Quantity = purchase.Quantity,
                ProductIds = purchase.Products,
                PurchaseToken = purchase.PurchaseToken,
                TransactionDateUtc = DateTimeOffset.FromUnixTimeMilliseconds(purchase.PurchaseTime).DateTime,
                ObfuscatedAccountId = purchase.AccountIdentifiers?.ObfuscatedAccountId,
                ObfuscatedProfileId = purchase.AccountIdentifiers?.ObfuscatedProfileId,
                TransactionIdentifier = purchase.PurchaseToken,
                State = purchase.PurchaseState switch
                {
                    Android.BillingClient.Api.PurchaseState.Pending => PurchaseState.PaymentPending,
                    Android.BillingClient.Api.PurchaseState.Purchased => PurchaseState.Purchased,
                    _ => PurchaseState.Unknown
                }
            };
            return finalPurchase;
        }
    }
}
