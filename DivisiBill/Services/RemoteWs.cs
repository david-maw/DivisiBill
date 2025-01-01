using CommunityToolkit.Mvvm.ComponentModel;
using DivisiBill.Models;
using System.Diagnostics;
using System.Text.Json;

namespace DivisiBill.Services;

/// <summary>
/// The public representation of data from the storage web service outside this 
/// </summary>
public partial class RemoteItemInfo : ObservableObject
{
    public string Name { get; set; }
    public DateTime CreatedDateTime => Utilities.DateTimeFromName(Name);
    public long Size { get; set; }
    public string CreatedDateTimeString => $"{CreatedDateTime:g} {Utilities.ApproximateAge(CreatedDateTime)}";
    public string SizeText => $"{Size / 1000.0:f1} kB";
    public string Description { get; set; } // An alias for the Summary field
    public bool ReplaceRequested { get; set; } = false;
    [ObservableProperty]
    public partial bool Selected { get; set; } = false;
}
/// <summary>
/// A bridge between the objects in DivisiBill (Meal, VenueList, PeopleList) and the more general methods in
/// the CallWs object. The remote item types are generally held in an 'itemTypeName' parameter to each function. 
/// </summary>
public static class RemoteWs
{
    static RemoteWs()
    {
        ItemTypeNameToPlural = new Dictionary<string, string>() { { PersonListTypeName, "People Lists" }, { VenueListTypeName, "Venue Lists" } };
    }
    #region Item Handling
    /// <summary>
    /// The layout of the data for each item delivered from the web service
    /// </summary>
    private class WsDataItem
    {
        public WsDataItem(string name, long dataLength, string data, string summary = null)
        {
            Name = name;
            DataLength = dataLength;
            Data = data;
            Summary = summary;
        }
        public string Name { get; set; }
        public string Data { get; set; }
        public long DataLength { get; set; }
        public string Summary { get; set; }
    }
    /// <summary>
    /// Get a list of all the remote items of a particular type.
    /// </summary>
    /// <param name="itemTypeName">The type of item to retrieve (for example, "meal" or "venuelist"</param>
    /// <returns>Either a list of items, possibly empty, or null if something goes wrong (like no Internet access)</returns>
    internal static async Task<List<RemoteItemInfo>> GetItemInfoListAsync(string itemTypeName)
    {
        const int MaxItems = 1000;
        if (string.IsNullOrWhiteSpace(itemTypeName))
            return null;
        List<RemoteItemInfo> remoteItemInfos = new List<RemoteItemInfo>();
        string latestName = "30000000000000"; // Start at the year 3000 or earlier 
        try
        {
            while (true)
            {
                var itemListJson = await CallWs.GetItemsStreamAsync(itemTypeName, MaxItems, latestName);
                if (itemListJson is not null && itemListJson.Length > 0)
                {
                    List<WsDataItem> items = JsonSerializer.Deserialize<List<WsDataItem>>(itemListJson);
                    foreach (var item in items)
                    {
                        remoteItemInfos.Add(new RemoteItemInfo()
                        {
                            Name = item.Name,
                            Size = item.DataLength,
                            Description = item.Summary
                        });
                    }
                    if (items.Count < MaxItems) // A truncated list, indicates we're out of items
                        break;
                    else
                        latestName = items.LastOrDefault()?.Name; // the next query starts where this left off
                }
                else
                    break;
            }
            return remoteItemInfos;
        }
        catch (Exception)
        {
            return null;
        }
    }
    /// <summary>
    /// Store the current Item list in the cloud using the current date and time as its name
    /// </summary>
    /// <param name="stream">The </param>
    /// <returns>A string (wrapped in a Task since it is Async), containing the ID of the uploaded file or null if it failed</returns>
    internal static async Task<bool> PutItemStreamAsync(string itemTypeName, Stream stream)
    {
        try
        {
            await App.CloudAllowedSource.WaitWhilePausedAsync();
            string name = Utilities.NameFromDateTime(DateTime.Now);
            string content = null;
            using (StreamReader reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 4096, true))
            {
                content = reader.ReadToEnd();
            }
            return await CallWs.PutItemAsync(itemTypeName, name, content);
        }
        catch (Exception)
        {
            if (Debugger.IsAttached)
                Debugger.Break();
            return false;
        }
    }
    /// <summary>
    /// Get a specific (or the latest) item from the list in the cloud
    /// </summary>
    /// <returns>A Stream (wrapped in a Task because it is an async method) or null if nothing was found</returns>
    internal static async Task<Stream> GetItemStreamAsync(string itemTypeName, string name)
    {
        try
        {
            // Allow a caller to provide no name and just get the latest item
            if (string.IsNullOrWhiteSpace(name))
            {
                var itemListJson = await CallWs.GetItemsStreamAsync(itemTypeName, 1);
                List<WsDataItem> items = await JsonSerializer.DeserializeAsync<List<WsDataItem>>(itemListJson);
                name = items.FirstOrDefault().Name;
                if (string.IsNullOrWhiteSpace(name))
                    return null;
            }

            Stream itemStream = await CallWs.GetItemAsStreamAsync(itemTypeName, name);
            return itemStream;
        }
        catch (Exception ex)
        {
            Utilities.DebugMsg($"Exception in GetStreamAsync for {itemTypeName} / {name} : {ex.Message}");
            return null;
        }
    }
    internal static async Task<bool> DeleteItemAsync(string itemTypeName, string name)
    {
        try
        {
            await App.CloudAllowedSource.WaitWhilePausedAsync();
            string result = await CallWs.DeleteItemAsync(itemTypeName, name);
            return true;
        }
        catch (Exception)
        {
            if (Debugger.IsAttached)
                Debugger.Break();
            return false;
        }
    }
    #endregion
    #region TypeName Constants
    public const string PersonListTypeName = "personlist";
    public const string VenueListTypeName = "venuelist";
    public const string MealTypeName = "meal";
    public static readonly Dictionary<string, string> ItemTypeNameToPlural;
    #endregion
    #region Meal

    /// <summary>
    /// Reach out to the web service to get an updated list of meals, delete the ones that have gone away (rarely any),
    /// and add the ones we don't know about, That means we ignore most meals we already know about 
    /// </summary>
    /// <returns>True if the meal list was loaded</returns>
    public static async Task<bool> GetRemoteMealListAsync()
    {
        List<RemoteItemInfo> remoteItems = null;
        remoteItems = await GetItemInfoListAsync(MealTypeName);
        if (remoteItems is null)
            return false;

        // Remove any bills which are no longer present in the cloud - there will rarely be any, so this isn't optimized
        Dictionary<string, RemoteItemInfo> remoteItemsDict = remoteItems.ToDictionary(ri => ri.Name);
        var missingList = Meal.RemoteMealList.Where(ms => !remoteItemsDict.ContainsKey(ms.Id)).ToList(); // a separate list because we're changing RemoteMealList
        foreach (var ms in missingList)
        {
            ms.IsRemote = false;
            Meal.RemoteMealList.Remove(ms);
        }

        // We have a list of the remote Meal items, so try and create a MealSummary for each of them
        int WholeFilesLoaded = 0;
        Dictionary<string, MealSummary> existingLocalMs = Meal.LocalMealList.ToDictionary(ms => ms.Id);
        Dictionary<string, MealSummary> existingRemoteMs = Meal.RemoteMealList.ToDictionary(ms => ms.Id);

        // Iterate through the remote items which are not known to us 
        foreach (var remoteItem in remoteItems.Where(ri => !existingRemoteMs.ContainsKey(ri.Name)))
        {
            if (existingLocalMs.TryGetValue(remoteItem.Name, out var ms))
            {
                // This MealSummary is already stored locally so just flag it as being remote as well and move on
                ms.IsRemote = true;
            }
            else
            {
                // This is a remote meal we have not seen before, so it's probably the first time this function has been called
                string description = remoteItem.Description;
                if (!string.IsNullOrEmpty(description))
                {
                    var buf = System.Text.Encoding.UTF8.GetBytes(description);
                    MemoryStream s = new MemoryStream(buf);
                    ms = MealSummary.LoadJsonFromStream(s);
                    if (ms is null)
                        Utilities.DebugMsg($"JSON load of description metadata failed, description = \"{description.TruncatedTo(30)}\"");
                    else
                        ms.IsRemote = true;
                }
                if (ms is null)
                {
                    // Either there was no description or it could not be decoded
                    if (WholeFilesLoaded++ < 10)
                        ms = await MealSummary.LoadFromRemoteMealAsync(remoteItem.Name); // just load up from the full Meal data
                    if (ms is null)
                    {
                        // Now we are really in trouble - neither the description nor the whole meal data could be decoded so return a fake MealSummary
                        ms = new MealSummary
                        {
                            IsRemote = true,
                            VenueName = "No Description",
                        };
                        ms.SetCreationTimeFromFileName(remoteItem.Name);
                    }
                }
                if (ms.FileNameInconsistent(remoteItem.Name))
                {
                    Utilities.DebugMsg($"Failed to load cloud Meal data {ms.Id}: file name inconsistent");
                    continue;
                }
                else
                    ms.Size = (long)remoteItem.Size;
            }
            Meal.RemoteMealList.Add(ms);
        }
        return true;
    }
    internal static async Task<bool> PutMealStreamAsync(MealSummary ms, Stream stream)
    {
        string mealData;
        using (StreamReader sr = new StreamReader(stream, System.Text.Encoding.UTF8, true, 4096, true))
        {
            mealData = sr.ReadToEnd();
        }
        return await CallWs.PutItemAsync(MealTypeName, ms.Id, mealData, ms.GetJsonString());
    }
    public static async Task DeleteMealAsync(MealSummary ms) => await CallWs.DeleteItemAsync(MealTypeName, ms.Id);
    #endregion
}
