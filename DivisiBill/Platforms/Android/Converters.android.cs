using Android.BillingClient.Api;
using DivisiBill.InAppBilling;
using AndroidPurchaseState = global::Android.BillingClient.Api.PurchaseState;
using IabPurchaseState = DivisiBill.InAppBilling.PurchaseState;

namespace DivisiBill.Platforms.Android;

internal static class Converters
{
    internal static InAppBillingPurchase ToIABPurchase(this Purchase purchase)
    {
        InAppBillingPurchase finalPurchase = new()
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
                AndroidPurchaseState.Pending => IabPurchaseState.PaymentPending,
                AndroidPurchaseState.Purchased => IabPurchaseState.Purchased,
                _ => IabPurchaseState.Unknown
            }
        };
        return finalPurchase;
    }
}
