using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Extensions;
using DivisiBill.InAppBilling;
using DivisiBill.Models;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
    private static readonly string KeyString = Generated.BuildInfo.DivisiBillWsKey;
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
    public static async Task<HttpResponseMessage> CallUncertainWebServiceAsync(Func<CancellationTokenSource, Task<HttpResponseMessage>> webCall)
    {
        var webStopwatch = Stopwatch.StartNew();
        CancellationTokenSource tokenSource = new();
        Task <HttpResponseMessage> webCallTask = webCall(tokenSource);
        await webCallTask.OrDelay(5000); // If it responds quickly, don't even bother to show a dialog
        // Call the web service and wait for a response or until the user gives up 
        if (webCallTask.IsCompleted && webCallTask.Result.IsSuccessStatusCode)
            return webCallTask.Result;
        else
        { // The call did not complete sucessfully in a timely manner, so show a popup to let the user know and give them a chance to abandon or retry it
            IPopupResult<HttpResponseMessage> popupResult = await Shell.Current.ShowPopupAsync<HttpResponseMessage>(new Controls.CheckWebPage(webCallTask, webCall, webStopwatch, tokenSource), Utilities.GetNullPopupOptions());
            return popupResult?.Result ?? new HttpResponseMessage(System.Net.HttpStatusCode.RequestTimeout); // If the user closed the popup, return a timeout result
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
        byte[] readFile = File.ReadAllBytes(ImagePath);
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
        using (StreamContent fileContent = new(imageStream))
        using (StringContent stringContent = new(Billing.OcrPurchase.OriginalJson, Encoding.UTF8, "application/json"))
        {
            MultipartFormDataContent multipartFormDataContent = new()
            {
                { stringContent, "license" },
                { fileContent, "fileContent", "bill-image-name" }
            };
            // Call the web service and store the response in a string
            content = await PostFormToScanAsync(multipartFormDataContent, cancel);
        }

        ScannedBill sb = System.Text.Json.JsonSerializer.Deserialize<ScannedBill>(content);
        if (sb is null)
            return null;
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
            HttpResponseMessage WsVersionTask = await CallUncertainWebServiceAsync((CancellationTokenSource cts) => client.GetAsync("version", cts.Token));

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
    /// 
    internal static async Task<bool> RecordPurchaseAsync(InAppBillingPurchase purchase)
    {
        if (DeviceInfo.Platform == DevicePlatform.Android)
        {
            // Store the license by calling a web service
            try
            {
                Dictionary<string, string> formData = new()
                {
                    { "purchase", purchase.OriginalJson },
                    { "signature", purchase.Signature }
                };
                FormUrlEncodedContent content = new(formData);
                HttpResponseMessage response = await client.PostAsync("RecordAndroidPurchase?subscription=", content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Utilities.DebugMsg("RecordPurchaseAsync failed, exception = " + ex);
            }
        }
        return false;
    }

    /// <summary>
    /// Verify that an InAppBilling purchase really is what it pretends to be by calling the issuer
    /// and also that we previously purchased it. Currently only implemented for Android.
    /// </summary>
    /// <param name="purchase">The InAppBilling object to be tested</param>
    /// <returns>The contents of the returned verification message or null if verification failed</returns>
    internal static async Task<string> VerifyPurchase(InAppBillingPurchase purchase)
    {
        Utilities.DebugMsg("In VerifyPurchase for " + purchase.Id);
        if (DeviceInfo.Platform == DevicePlatform.Android || (DeviceInfo.Platform == DevicePlatform.WinUI && Utilities.IsDebug))
        {
            try
            {
                Dictionary<string, string> formData = new()
                {
                    { "purchase", purchase.OriginalJson },
                    { "signature", purchase.Signature }
                };
                FormUrlEncodedContent content = new(formData);
                Utilities.DebugMsg("In VerifyPurchase, awaiting VerifyAndroidPurchase");
                // validate the license by calling a web service
                HttpResponseMessage response = await CallUncertainWebServiceAsync((CancellationTokenSource cts) => client.PostAsync("VerifyAndroidPurchase", content, cts.Token));
                if (response.IsSuccessStatusCode)
                {
                    string s = await response.Content.ReadAsStringAsync();
                    Utilities.RecordMsg("In VerifyPurchase, VerifyAndroidPurchase returned ok and \"" + s + "\"");
                    // If this is a pro license, pass it to future web service calls for authorization
                    if (purchase.ProductId.Equals(Billing.ProSubscriptionId) || purchase.ProductId.Equals(Billing.OldProProductId))
                    {
                        UpsertHttpClientHeader(PurchaseHeaderName, purchase.OriginalJson); // This will be the license used from now on
                        response.StoreTokenHeader();
                    }
                    return s;
                }
                else
                    Utilities.RecordMsg("In VerifyPurchase, verify returned status " + (int)response.StatusCode + "-" + response.StatusCode + " and '" + await response.Content.ReadAsStringAsync() + "'");
            }
            catch (Exception ex)
            {
                Utilities.RecordMsg("Exception in VerifyPurchase for " + purchase.Id + ": " + ex.Message);
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
    /// <param name="itemTypeName">The item type (<see cref="RemoteWs.MealTypeName"/> for example</param>
    /// <param name="id">Name of the item to be retrieved</param>
    /// <returns>The item data (even for meal items), normally an XML encoded object</returns>
    /// 
    public static async Task<string> GetItemDataAsStringAsync(string itemTypeName, string id)
    {
        HttpResponseMessage response = await client.GetAsync($"{itemTypeName}/{id}");
        if (response.IsSuccessStatusCode)
        {
            StoreTokenHeader(response);
            bool isEncrypted = string.Equals(response.Content.Headers.ContentType.MediaType, "application/octet-stream");
            if (isEncrypted)
            {
                byte[] encryptedBytes = await response.Content.ReadAsByteArrayAsync();
                byte[] plaintextBytes = await Task.Run(() => CryptManager.DecryptToBytes(encryptedBytes));
                return Encoding.UTF8.GetString(plaintextBytes);
            }
            else
                return await response.Content.ReadAsStringAsync();
        }
        else
            return null;
    }
    public static async Task<Stream> GetItemDataAsStreamAsync(string itemTypeName, string id)
    {
        HttpResponseMessage response = await client.GetAsync($"{itemTypeName}/{id}");
        if (response.IsSuccessStatusCode)
        {
            StoreTokenHeader(response);
            bool isEncrypted = string.Equals(response.Content.Headers.ContentType.MediaType, "application/octet-stream");
            if (isEncrypted)
            {
                byte[] encryptedBytes = await response.Content.ReadAsByteArrayAsync();
                byte[] plaintextBytes = await Task.Run(() => CryptManager.DecryptToBytes(encryptedBytes));
                return new MemoryStream(plaintextBytes);
            }
            else
            {
                return await response.Content.ReadAsStreamAsync();
            }
        }
        else
            return null;
    }
    /// <summary>
    /// Store a single item by sending multiple form fields
    /// </summary>
    /// <param name="itemTypeName">The item type (<see cref="RemoteWs.MealTypeName"/> for example</param>
    /// <param name="id">Name of the item</param>
    /// <param name="itemData">Data associated with the item</param>
    /// <param name="itemSummary">Summary data for the item (valid only for meal items</param>
    /// <returns>true of the put worked, false if not</returns>
    public static async Task<bool> PutItemAsync(string itemTypeName, string id, string itemData, string itemSummary = null)
    {
        if (CryptManager.HasStoredPassword && !CryptManager.HasStoredRsa)
            throw new CryptographicException("Unable to access stored key");

        using MultipartFormDataContent multipartFormDataContent = new()
        {
            await FormContent("data", itemData)
        };
        if (itemSummary != null)
            multipartFormDataContent.Add(await FormContent("summary", itemSummary));

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

        // Local function to create form content for a field
        async Task<HttpContent> FormContent(string fieldName, string fieldValue)
        {
            HttpContent itemDataContent;
            // Optionally load RSA for encryption
            using RSA rsa = await CryptManager.GetStoredRsaFromFingerprintAsync();
            // Build data content (encrypt if RSA available)
            if (rsa is not null)
            {
                byte[] plaintext = Encoding.UTF8.GetBytes(fieldValue);
                byte[] encrypted = await Task<byte[]>.Run(() => CryptManager.EncryptToBytes(plaintext, rsa));
                itemDataContent = new ByteArrayContent(encrypted);
                itemDataContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                itemDataContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
                {
                    Name = fieldName,
                    FileName = fieldName
                };
            }
            else
            {
                itemDataContent = new StringContent(fieldValue, Encoding.UTF8, "application/xml");
                itemDataContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = fieldName };
            }
            return itemDataContent;
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
        HttpContent content = await GetItemsAsync(itemTypeName, top, before);
        return await content.ReadAsStringAsync();
    }
    public static async Task<Stream> GetItemsStreamAsync(string itemTypeName, int top = 50, string before = "30000000000000")
    {
        HttpContent content = await GetItemsAsync(itemTypeName, top, before);
        return await content.ReadAsStreamAsync();
    }
    private static async Task<HttpContent> GetItemsAsync(string itemTypeName, int top, string before)
    {
        string param = "?top=" + top.ToString();
        if (!string.IsNullOrWhiteSpace(before))
            param += "&before=" + before;
        HttpResponseMessage response = await client.GetAsync(itemTypeName + "s" + param);
        StoreTokenHeader(response);
        HttpContent temp = response.Content;
        return temp;
    }
    public static async Task<string> DeleteAllItemsAsync(string itemTypeName)
    {
        HttpResponseMessage response = await client.DeleteAsync(itemTypeName + "s");
        StoreTokenHeader(response);
        string temp = await response.Content.ReadAsStringAsync();
        return temp;
    }
    #endregion
    #region Image Files
    public static async Task<HttpResponseMessage> UploadFileAsync(string filePath)
    {
        try
        {
            // Local function to detect a few common MIME types based on the file extension
            static string GetMediaTypeFromName(string fname) => fname switch
            {
                var f when f.EndsWith(".enc", StringComparison.OrdinalIgnoreCase) => "application/octet-stream",
                var f when f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) => "application/pdf",
                var f when f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) => "text/plain",
                var f when f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                       || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) => "image/jpeg",
                var f when f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) => "image/png",
                _ => "application/octet-stream"
            };

            Stream fileStream = File.OpenRead(filePath);
            string blobName = Path.GetFileName(filePath);
            // Detect a few common MIME types based on the file extension
            using RSA rsa = CryptManager.HasStoredPassword ? await CryptManager.GetStoredRsaFromFingerprintAsync() : null;
            if (rsa is not null)
            {
                MemoryStream encrypted = new();
                await CryptManager.EncryptAsync(fileStream, encrypted, rsa);
                encrypted.Position = 0;
                fileStream.Close();
                fileStream.Dispose();
                fileStream = encrypted;
                blobName += ".enc";
            }
            using StreamContent content = new(fileStream);
            content.Headers.ContentType = new MediaTypeHeaderValue(GetMediaTypeFromName(blobName));

            using MultipartFormDataContent form = new()
            {
                { content, "file", blobName }
            };
            return await client.PostAsync("file", form);
        }
        catch (Exception)
        {
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent($"Fault in {nameof(UploadFileAsync)}.")
            };
        }
    }

    public static async Task<bool> DownloadFileAsync(string fileNameValue, string savePath, bool isEncrypted)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(savePath));
        string blobNameValue = fileNameValue + (isEncrypted ? ".enc" : string.Empty); // Add .enc at the end if necessary
        try
        {
            HttpResponseMessage response = await client.GetAsync($"file/{Uri.EscapeDataString(blobNameValue)}", HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using Stream responseStream = await response.Content.ReadAsStreamAsync();
            using FileStream fileStream = File.Create(savePath);
            if (isEncrypted)
            {
                using MemoryStream decrypted = new();
                await CryptManager.DecryptAsync(responseStream, decrypted);
                decrypted.Position = 0;
                await decrypted.CopyToAsync(fileStream);
            }
            else
                await responseStream.CopyToAsync(fileStream);
            return true;
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Download failed", ex.Message, "OK");
        }
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

        JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
        List<ImageItem> items = await httpResponse.Content.ReadFromJsonAsync<List<ImageItem>>(options) ?? [];
        return items;
    }
    public static async Task<string> DeleteAllFilesAsync()
    {
        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await client.DeleteAsync("files");
            if (await httpResponse.IsGoodAsync())
                return await httpResponse.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            Utilities.RecordMsg($"Request failed: {ex.Message}");
        }
        return string.Empty;
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
