using DivisiBill.Models;
using Plugin.InAppBilling;
using System.Net;
using System.Text;

namespace DivisiBill.Services;

/// <summary>
/// A Convenient Container for Web Services Call Logic
/// </summary>
internal static class CallWs
{
    #region Shared
    const string PurchaseHeaderName = "divisibill-android-purchase";
    const string TokenHeaderName = "divisibill-token";
    const string KeyHeaderName = "x-functions-key";

    static HttpClient client = new HttpClient() { BaseAddress = new Uri(Generated.BuildInfo.DivisiBillWsUri) };

    public static Uri BaseAddress => client.BaseAddress;

    static CallWs()
    {
        if (!App.WsAllowed)
            throw new ArgumentNullException("App.WsAllowed");
        if (!string.IsNullOrWhiteSpace(Generated.BuildInfo.DivisiBillWsKey))
            UpsertHttpClientHeader(KeyHeaderName, Generated.BuildInfo.DivisiBillWsKey);
    }

    #region Header Management
    private static void StoreTokenHeader(this HttpResponseMessage response)
    {
        string tokenValue = response.Headers.Contains(TokenHeaderName) ? response.Headers.GetValues(TokenHeaderName).FirstOrDefault() : null;
        if (!string.IsNullOrWhiteSpace(tokenValue))
            UpsertHttpClientHeader(TokenHeaderName, tokenValue);
    }
    private static void UpsertHttpClientHeader(string headerName, string headerValue)
    {
        if (client.DefaultRequestHeaders.Contains(headerName))
            client.DefaultRequestHeaders.Remove(headerName);
        client.DefaultRequestHeaders.Add(headerName, headerValue);
    }
    #endregion
    #endregion
    #region Scan a Bill
    /// <summary>
    /// Scan a bill image (usually a JPG file) and return the results in a ScannedBill object
    /// </summary>
    /// <param name="ImagePath">The full path to the file containing the image to scan</param>
    /// <param name="cancel">A CancellationToken used to stop an in-process scan</param>
    /// <returns>A ScannedBill object indicating the number of scans left on the license used and the contents of the scan</returns>
    /// <exception cref="OperationCanceledException"></exception>
    internal static Task<ScannedBill> ImageToScannedBill(string ImagePath, CancellationToken cancel)
    {
        var readFile = File.ReadAllBytes(ImagePath);
        MemoryStream stream = new MemoryStream(readFile);
        cancel.ThrowIfCancellationRequested();
        return ImageToScannedBill(stream, cancel);
    }
    /// <summary>
    /// Scan a bill image stream (usually JPG) and return the results in a ScannedBill object.
    /// This is done by calling a web service which also requires a valid license so we must select one
    /// and pass it with the web service call.
    /// </summary>
    /// <param name="imageStream">The image to scan (usually JPEG, sometimes PNG)</param>
    /// <param name="cancel">A CancellationToken used to stop an in-process scan</param>
    /// <returns>A ScannedBill object indicating the number of scans left on the license used and the contents of the scan</returns>
    private static async Task<ScannedBill> ImageToScannedBill(Stream imageStream, CancellationToken cancel)
    {
        if (Billing.ScansLeft <= 0)
            return null;
        string content = null;
        // Create a multi part form data content message body and send it
        using (var fileContent = new StreamContent(imageStream))
        using (var stringContent = new StringContent(Billing.OcrPurchase.OriginalJson, Encoding.UTF8, "application/json"))
        {
            var multipartFormDataContent = new MultipartFormDataContent
            {
                { stringContent, "license" },
                { fileContent, "fileContent", "bill-image-name" }
            };
            // Call the web service and store the response in a string
            content = await PostFormToScanAsync(multipartFormDataContent, cancel);
        }

        var sb = System.Text.Json.JsonSerializer.Deserialize<ScannedBill>(content);
        if (sb is null) return null;
        // Now set the scans remaining count
        Billing.ScansLeft = sb.ScansLeft;
        return sb;
    }
    private static async Task<String> PostFormToScanAsync(MultipartFormDataContent form, CancellationToken cancel)
    {
        // Check if there is Internet connectivity
        if (Connectivity.NetworkAccess != Microsoft.Maui.Networking.NetworkAccess.Internet)
        {
            throw new IOException("No Internet access");
        }
        string responseData = null;
        try
        {
            // Send image data to the server and return the response text
            // The query parameter 'option' tells the web service to do a couple of different things depending on the value, specifically:
            //    "1" - Return an error and multi line error text describing the message it received
            //    "2" - Return a fake ScannedBill without ever calling an OCR function
            //    Other values are ignored and the normal OCR functions are performed
            int option = Utilities.IsDebug ? App.ScanOption : 0;
            HttpResponseMessage response = await client.PostAsync($"scan?option={option}", form);
            responseData = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();
            response.StoreTokenHeader();
            return responseData;
        }
        catch (Exception ex)
        {
            if (string.IsNullOrEmpty(responseData))
                throw;
            else
                throw new HttpRequestException(ex.Message + "\n\n" + System.Text.RegularExpressions.Regex.Unescape(responseData), ex);
        }
    }
    #endregion
    #region Get Version

    public static string MostRecentVersionInfo { get; set; } = null;

    /// <summary>
    /// Get the version of various server-side components
    /// </summary>
    /// <returns>A string containing the various versions in use on the server</returns>
    internal static async Task<HttpStatusCode> GetVersion()
    {
        try
        {
            HttpResponseMessage response =
                  await client.GetAsync("version");

            if (response.IsSuccessStatusCode)
            {
                MostRecentVersionInfo = await response.Content.ReadAsStringAsync();
                // Detect the weird failure which just returns an OK result but no data
                if (string.IsNullOrEmpty(MostRecentVersionInfo))
                    return HttpStatusCode.NotFound;
            }

            return response.StatusCode;
        }
        catch (Exception ex)
        {
            Utilities.DebugMsg("GetVersion failed, exception = " + ex);
            return HttpStatusCode.ServiceUnavailable;
        }
    }
    #endregion
    #region Purchase and Verify
    /// <summary>
    /// Make a record of a new purchase
    /// </summary>
    /// <param name="purchase"></param>
    /// <returns>True if the purchase was recorded, false if not</returns>
    /// <param name="isSubscription"></param>
    internal static async Task<bool> RecordPurchaseAsync(InAppBillingPurchase purchase, bool isSubscription)
    {
        if (DeviceInfo.Platform == DevicePlatform.Android)
        {
            // validate the license by calling a web service
            try
            {
                HttpResponseMessage response = await client.PostAsync("recordpurchase?subscription=" + (isSubscription ? "1" : "0"),
                            new StringContent(purchase.OriginalJson, Encoding.UTF8, "application/json"));
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Utilities.DebugMsg("RecordPurchaseAsync failed, exception = " + ex);
            }
        }
        return false;
    }

#if DEBUG
    /// <summary>
    /// A version of <see cref="VerifyPurchase"/> for testing using predefined android licenses in a debug build
    /// Verify that an InAppBilling purchase really is what it pretends to be by calling the issuer
    /// and also that we previously purchased it. Currently only implemented for Android.
    /// </summary>
    /// <returns>The contents of the returned message or null if verification failed</returns>
    /// <param name="androidJson">The android license to be tested</param>
    /// <param name="productId">The productId the license is for (it's in the json but we'd need to decode it)</param>
    /// <param name="isSubscription">True of this is a Subscription, false for a consumable license</param>
    internal static async Task<string> VerifyAndroidPurchase(string androidJson, string productId, bool isSubscription)
    {
        Utilities.DebugMsg("In VerifyAndroidPurchase for " + productId);
        if (DeviceInfo.Platform == DevicePlatform.Android || (DeviceInfo.Platform == DevicePlatform.WinUI && Utilities.IsDebug))
        {
            Utilities.DebugMsg("In VerifyAndroidPurchase, awaiting verify");
            // validate the license by calling a web service
            var response = await client.PostAsync("verify?subscription=" + (isSubscription ? "1" : "0"),
                new StringContent(androidJson, System.Text.Encoding.UTF8, "application/json"));
            if (response.IsSuccessStatusCode)
            {
                string s = await response.Content.ReadAsStringAsync();
                Utilities.DebugMsg("In VerifyAndroidPurchase, verify returned ok and \"" + s + "\"");
                // If this is a pro license, pass it to future web service calls for authorization
                if (productId.Equals(Billing.ProSubscriptionId) || productId.Equals(Billing.OldProProductId))
                {
                    // The fake JSON string may be delimited by CR/LF, if it is just remove them because CR/LF are not allowed in headers
                    string flatJson = androidJson.Replace("\r\n", string.Empty);
                    UpsertHttpClientHeader(PurchaseHeaderName, flatJson); // This will be the license used from now on
                    response.StoreTokenHeader();
                }
                return s;
            }
            else
                Utilities.DebugMsg("In VerifyAndroidPurchase, verify returned " + response.StatusCode);
        }
        return null;
    }
#endif
    /// <summary>
    /// Verify that an InAppBilling purchase really is what it pretends to be by calling the issuer
    /// and also that we previously purchased it. Currently only implemented for Android.
    /// </summary>
    /// <param name="purchase">The InAppBilling object to be tested</param>
    /// <returns>The contents of the returned verification message or null if verification failed</returns>
    internal static async Task<string> VerifyPurchase(InAppBillingPurchase purchase, bool isSubscription)
    {
        Utilities.DebugMsg("In VerifyPurchase for " + purchase.Id);
        if (DeviceInfo.Platform == DevicePlatform.Android || (DeviceInfo.Platform == DevicePlatform.WinUI && Utilities.IsDebug))
        {
            try
            {
                Utilities.DebugMsg("In VerifyPurchase, awaiting verify");
                // validate the license by calling a web service
                var response = await client.PostAsync("verify?subscription=" + (isSubscription ? "1" : "0"),
                    new StringContent(purchase.OriginalJson, Encoding.UTF8, "application/json"));
                if (response.IsSuccessStatusCode)
                {
                    string s = await response.Content.ReadAsStringAsync();
                    Utilities.DebugMsg("In VerifyPurchase, verify returned ok and \"" + s + "\"");
                    // If this is a pro license, pass it to future web service calls for authorization
                    if (purchase.ProductId.Equals(Billing.ProSubscriptionId) || purchase.ProductId.Equals(Billing.OldProProductId))
                    {
                        UpsertHttpClientHeader(PurchaseHeaderName, purchase.OriginalJson); // This will be the license used from now on
                        response.StoreTokenHeader();
                    }
                    return s;
                }
                else
                    Utilities.DebugMsg("In VerifyPurchase, verify returned " + response.StatusCode);
            }
            catch (Exception ex)
            {
                Utilities.DebugMsg("Exception in VerifyPurchase for " + purchase.Id + ": " + ex.Message);
            }
        }
        else
            Utilities.DebugMsg("In VerifyPurchase, not Android");
        Utilities.DebugMsg("Leaving VerifyPurchase, returning null");
        return null;
    }
    #endregion
    #region CRUD operations on Meal/VenueList/PersonList
    /// <summary>
    /// Get a single item (Meal, PersonList or VenueList)
    /// </summary>
    /// <param name="itemTypeName">The item type ("meal"/VenueListTypeName/"personlist")</param>
    /// <param name="id">Name of the item to be retrieved</param>
    /// <returns>The item data (even for meal items), normally an XML encoded object</returns>
    public static async Task<string> GetItemAsStringAsync(string itemTypeName, string id)
    {
        HttpResponseMessage response = await client.GetAsync($"{itemTypeName}/{id}");
        if (response.IsSuccessStatusCode)
        {
            StoreTokenHeader(response);
            string temp = await response.Content.ReadAsStringAsync();
            return temp;
        }
        else
            return null;
    }
    public static async Task<Stream> GetItemAsStreamAsync(string itemTypeName, string id)
    {
        HttpResponseMessage response = await client.GetAsync($"{itemTypeName}/{id}");
        if (response.IsSuccessStatusCode)
        {
            StoreTokenHeader(response);
            Stream temp = await response.Content.ReadAsStreamAsync();
            return temp;
        }
        else
            return null;
    }
    /// <summary>
    /// Store a single item
    /// </summary>
    /// <param name="itemTypeName">The item type ("meal"/VenueListTypeName/"personlist")</param>
    /// <param name="id">Name of the item</param>
    /// <param name="itemData">Data associated with the item</param>
    /// <param name="itemSummary">Summary data for the item (valid only for meal items</param>
    /// <returns>true of the put worked, false if not</returns>
    public static async Task<bool> PutItemAsync(string itemTypeName, string id, string itemData, string itemSummary = null)
    {
        // Create a multipart form data content message body and send it
        using (var itemDataContent = new StringContent(itemData, Encoding.UTF8, "application/xml"))
        {
            var multipartFormDataContent = new MultipartFormDataContent();

            StringContent itemSummaryContent = null;
            if (itemSummary is not null)
                multipartFormDataContent.Add(itemSummaryContent = new StringContent(itemSummary, Encoding.UTF8, "application/json"), "summary");
            multipartFormDataContent.Add(itemDataContent, "data");
            // Call the web service and show the response 
            string responseData = null;
            try
            {
                HttpResponseMessage response = await client.PutAsync($"{itemTypeName}/{id}", multipartFormDataContent);
                StoreTokenHeader(response);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(responseData))
                    throw;
                else
                    throw new HttpRequestException(ex.Message + "\n\n" + System.Text.RegularExpressions.Regex.Unescape(responseData), ex);
            }
            finally
            {
                if (itemSummaryContent is not null)
                    itemSummaryContent.Dispose();
                multipartFormDataContent.Dispose();
            }
        }
    }
    public static async Task<string> DeleteItemAsync(string itemTypeName, string id)
    {
        HttpResponseMessage response = await client.DeleteAsync($"{itemTypeName}/{id}");
        StoreTokenHeader(response);
        string temp = await response.Content.ReadAsStringAsync();
        return temp;
    }
    public static async Task<string> GetItemsStringAsync(string itemTypeName, int top = 50, string before = "30000000000000")
    {
        var content = await GetItemsAsync(itemTypeName, top, before);
        return await content.ReadAsStringAsync();
    }
    public static async Task<Stream> GetItemsStreamAsync(string itemTypeName, int top = 50, string before = "30000000000000")
    {
        var content = await GetItemsAsync(itemTypeName, top, before);
        return await content.ReadAsStreamAsync();
    }
    private static async Task<HttpContent> GetItemsAsync(string itemTypeName, int top, string before)
    {
        string param = "?top=" + top.ToString();
        if (!string.IsNullOrWhiteSpace(before))
            param += "&before=" + before;
        HttpResponseMessage response = await client.GetAsync(itemTypeName + "s" + param);
        StoreTokenHeader(response);
        var temp = response.Content;
        return temp;
    }
    #endregion

}
