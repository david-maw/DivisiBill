using CommunityToolkit.Maui.Extensions;
using DivisiBill.Models;
using Plugin.InAppBilling;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace DivisiBill.Services;

/// <summary>
/// A Convenient Container for Web Services Call Logic
/// </summary>
internal static class CallWs
{
    #region Shared
    private const string PurchaseHeaderName = "divisibill-android-purchase";
    private const string TokenHeaderName = "divisibill-token";
    private const string KeyHeaderName = "x-functions-key";
    private static string KeyString = Generated.BuildInfo.DivisiBillWsKey;
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);
    private static readonly HttpClient client = new() { BaseAddress = new Uri(Generated.BuildInfo.DivisiBillWsUri), Timeout = CallTimeout };

    public static Uri BaseAddress => client.BaseAddress;

    #region Class Constructor
    static CallWs()
    {
        if (!App.WsUriDefined)
            throw new ArgumentNullException("App.WsUriDefined");
        if (!string.IsNullOrWhiteSpace(Generated.BuildInfo.DivisiBillWsKey))
            UpsertHttpClientHeader(KeyHeaderName, KeyString);
    }
    #endregion

    /// <summary>
    /// Invoke a web service, wait a few seconds to see if it completes, then pop up a dialog so the user can check progress and abandon it as needed.
    /// </summary>
    /// <param name="webCall">The function to call and timeout if necessary</param>
    /// 
    /// <returns></returns>
    internal static async Task<HttpResponseMessage> CallUncertainWebServiceAsync(Func<Task<HttpResponseMessage>> webCall)
    {
        Stopwatch webStopwatch = Stopwatch.StartNew();
        Task<HttpResponseMessage> webCallTask = webCall();
        await webCallTask.OrDelay(5000); // If it responds quickly, don't even bother to show a dialog
        // Call the web service and wait for a response or until the user gives up 
        if (webCallTask.IsCompleted && webCallTask.Result.IsSuccessStatusCode)
            return webCallTask.Result;
        else
        { // The call did not complete successfully, so show a popup to let the user know and give them a chance to retry or abandon it}
            var popupResult = await Shell.Current.ShowPopupAsync<HttpResponseMessage>(new Views.CheckWebPage(webCallTask, webCall, webStopwatch), Utilities.GetNullPopupOptions());
            return popupResult?.Result ?? new HttpResponseMessage(System.Net.HttpStatusCode.RequestTimeout); // If the user closed the popup without retrying, return a timeout result
        }
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
    #region Select Alternate Web Service (debug only) 
    [Conditional("DEBUG")]
    internal static void SelectAlternateWs()
    {
        string wsSuffix = Environment.GetEnvironmentVariable("DIVISIBILL_WS_USE");
        if (string.IsNullOrWhiteSpace(wsSuffix))
            return; // No new one requested, just leave the default alone
        wsSuffix = wsSuffix.ToUpperInvariant();
        string wsUriText;
        string wsKeyText;
        switch (wsSuffix)
        {
            case "ALTERNATE":
                wsUriText = Environment.GetEnvironmentVariable("DIVISIBILL_ALTERNATE_WS_URI");
                wsKeyText = Environment.GetEnvironmentVariable("DIVISIBILL_ALTERNATE_WS_KEY");
                break;
            default: // Usually "release"
                wsUriText = Environment.GetEnvironmentVariable("DIVISIBILL_WS_URI_" + wsSuffix);
                wsKeyText = Environment.GetEnvironmentVariable("DIVISIBILL_WS_KEY_" + wsSuffix);
                break;
        }

        if (!string.IsNullOrWhiteSpace(wsUriText) && !string.IsNullOrWhiteSpace(wsKeyText))
        {
            Uri newUri = new(wsUriText);
            try
            {
                client.BaseAddress = newUri;
                KeyString = wsKeyText;
                UpsertHttpClientHeader(KeyHeaderName, KeyString);
                Utilities.DebugMsg("SelectAlternateWs changed the BaseAddress to DIVISIBILL_WS..." + wsSuffix + " environment variable");
            }
            catch (Exception ex)
            {
                Utilities.DebugMsg("SelectAlternateWs failed to change the BaseAddress to " + wsUriText + "exception:" + ex.Message);
            }
            return;
        }
    }
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
        MemoryStream stream = new(readFile);
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
    private static async Task<string> PostFormToScanAsync(MultipartFormDataContent form, CancellationToken cancel)
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
            HttpResponseMessage response = await client.PostAsync($"scan?option={option}", form, cancel);
            responseData = await response.Content.ReadAsStringAsync(cancel);
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

    internal static string MostRecentVersionInfo { get; set; } = null;

    /// <summary>
    /// Get the version of various server-side components
    /// </summary>
    /// <returns>A string containing the various versions in use on the server</returns>
    internal static async Task<bool> GetVersionAsync()
    {
        bool WsVersionChecked = false;
        try
        {
            HttpResponseMessage WsVersionTask = await CallUncertainWebServiceAsync(() => client.GetAsync("version"));

            if (WsVersionTask is not null && WsVersionTask.IsSuccessStatusCode)
            {
                MostRecentVersionInfo = await WsVersionTask.Content.ReadAsStringAsync();
                // Detect the weird failure which just returns an OK result but no data
                if (string.IsNullOrEmpty(MostRecentVersionInfo))
                { // This is a failure, return a NotFound status
                    Utilities.DebugMsg("GetVersion returned OK but no data, returning NotFound");
                }
                else
                    WsVersionChecked = true;
            }
            else if (WsVersionTask is null)
                Utilities.DebugMsg("GetVersion failed, no task returned");
            else
                Utilities.DebugMsg("GetVersion failed, status code = " + WsVersionTask.StatusCode);
        }
        catch (Exception ex)
        {
            Utilities.DebugMsg("GetVersion failed, exception = " + ex);
        }
        return WsVersionChecked;
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
            HttpResponseMessage response = await CallUncertainWebServiceAsync(() => client.PostAsync("verify?subscription=" + (isSubscription ? "1" : "0"),
                new StringContent(androidJson, System.Text.Encoding.UTF8, "application/json")));
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
                Utilities.DebugMsg("In VerifyAndroidPurchase, verify returned status code " + response.StatusCode);
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
                var response = await CallUncertainWebServiceAsync(() => client.PostAsync("verify?subscription=" + (isSubscription ? "1" : "0"),
                    new StringContent(purchase.OriginalJson, Encoding.UTF8, "application/json")));
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
        using var itemDataContent = new StringContent(itemData, Encoding.UTF8, "application/xml");
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
            itemSummaryContent?.Dispose();
            multipartFormDataContent.Dispose();
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
    #region Image Files
    public static async Task<HttpResponseMessage> UploadFileAsync(string filePath)
    {
        using var form = new MultipartFormDataContent();
        using var fileStream = File.OpenRead(filePath);
        var fileName = Path.GetFileName(filePath);
        // Detect a few common MIME types based on the file extension
        string mediaType = fileName switch
        {
            var f when f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) => "text/plain",
            var f when f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) => "image/jpeg",
            var f when f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) => "image/jpeg",
            var f when f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) => "image/png",
            _ => "application/octet-stream"
        };
        var fileContent = new StreamContent(fileStream)
        {
            Headers =
            {
                ContentType = new MediaTypeHeaderValue(mediaType),
                ContentDisposition = new ContentDispositionHeaderValue("form-data")
                {
                    Name = "\"file\"",
                    FileName = "\"" + fileName + "\""
                }
            }
        };
        form.Add(fileContent);
        return await client.PostAsync("file", form);
    }

    public static async Task<bool> DownloadFileAsync(string fileName, string savePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(savePath));
        HttpResponseMessage response = await client.GetAsync($"file/{fileName}");
        if (response.IsSuccessStatusCode)
        {
            var fileBytes = await response.Content.ReadAsByteArrayAsync();
            try
            {
                await File.WriteAllBytesAsync(savePath, fileBytes);
                Utilities.DebugMsg($"Downloaded to {savePath}");
                return true;
            }
            catch (Exception ex)
            {
                Utilities.ReportCrash(ex, $"In {nameof(DownloadFileAsync)}: Download Failed");
                return false;
            }
        }
        else
            Utilities.RecordMsg($"In {nameof(DownloadFileAsync)}: network error: {response.StatusCode}");
        return false;
    }

    public static async Task<bool> DeleteFileAsync(string fileName)
    {
        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await client.DeleteAsync($"file/{fileName}");
            return await httpResponse.IsGoodAsync();
        }
        catch (Exception ex)
        {
            Utilities.RecordMsg($"Request failed: {ex.Message}");
            return false;
        }
    }
    public static async Task<List<ImageItem>> EnumerateFilesAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await client.GetAsync("files");
            if (!await httpResponse.IsGoodAsync())
                return null;
        }
        catch (Exception ex)
        {
            Utilities.RecordMsg($"Request failed: {ex.Message}");
            return null;
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
        var items = await httpResponse.Content.ReadFromJsonAsync<List<ImageItem>>(options) ?? new List<ImageItem>();
        return items;
    }
    #endregion
}
public sealed class ImageItem
{
    public string name { get; set; }
    public string contentType { get; set; }
    public long size { get; set; }
    public DateTimeOffset? lastModified { get; set; }
}
