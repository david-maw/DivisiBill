// Ignore Spelling: Deserialize

using CommunityToolkit.Mvvm.ComponentModel;
using DivisiBill.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Xml;
using System.Xml.Serialization;

namespace DivisiBill.Models;

[DebuggerDisplay("{Name}")]
public partial class Venue : ObservableObject, IComparable<Venue>
{
    public const string VenueFolderName = "Venues";
    public const string VenueFileName = "Venues.xml";
    public static event EventHandler<VenueDistanceChangedEventArgs> DistanceChanged;

    private static readonly string VenueFullName = Path.Combine(App.BaseFolderPath, VenueFolderName, VenueFileName);
    private readonly Location MiddleOfNowhere = new(20, 170); // Middle of the Pacific, not close to anything

    private static readonly ObservableCollection<Venue> allVenues = [];
    private static readonly ObservableCollection<Venue> allVenuesByDistance = [];
    private static bool allVenuesByDistanceIsSorted = true;
    public static Venue Current
    {
        get => field;
        private set
        {
            if (!ReferenceEquals(field, value))
            {
                field?.IsForCurrentMeal = false;
                value?.IsForCurrentMeal = true;
                field = value;
            }
        }
    }
    private static void LoadDefaultVenues()
    {
        List<Venue> initialVenues = [
            new() {Name = "Queasy Diner",             Latitude= 20.79, Longitude = -156.24, Accuracy = 700},
            new() {Name = "Bad Pizza",                Latitude= 33.6120, Longitude = -117.7080, Accuracy = 10},
            new() {Name = "Too Much Food"},
            new() {Name = "Bad Burgers"},
            new() {Name = "Really bad burgers"},
        ];
        initialVenues.Sort();
        foreach (Venue v in initialVenues)
            allVenues.Add(v);
        initialVenues.Sort((v1, v2) => v1.CompareDistanceTo(v2));
        foreach (Venue v in initialVenues)
            allVenuesByDistance.Add(v);
        MarkSaved(); // Flag this as not needing to be saved so it won't be unless someone changes it 
    }
    public static async Task InitializeAsync()
    {
        bool loaded = false;
        InitializeFolders();
        if (File.Exists(VenueFullName))
            loaded = await LoadFromLocal();
        if (!loaded && App.IsCloudAllowed)
            loaded = await LoadFromRemoteAsync(null, true); // Pass a null filename to just load the latest
        if (!loaded)
            LoadDefaultVenues();
        allVenues.CollectionChanged += (s, e) =>
        {
            UpdateTime = DateTime.Now;
        };
    }

    private static readonly XmlSerializer allVenuesSerializer = new(typeof(VenueRoot));
    private static async Task<bool> LoadFromStreamAsync(Stream stream, bool replace)
    {
        if (stream is null)
            return false;
        else
            try
            {
                Updater = App.Settings.VenueUpdater;
                if (Updater == Guid.Empty)
                    Updater = App.Current.Id; // Set the current app id
                Utilities.DebugExamineStream(stream);
                MergeVenues(stream, replace);
                await Task.Delay(100); // Avoids a "no async" warning
                return true;
            }
            catch (Exception ex)
            {
                ex.ReportCrash();
            }
        return false;
    }

    public static async Task<bool> LoadFromRemoteAsync(string name, bool replace)
    {
        Stream stream = null;
        if (App.IsCloudAllowed)
            stream = await RemoteWs.GetItemStreamAsync(RemoteWs.VenueListTypeName, name);
        if (stream is null)
            return false;
        else
            try
            {
                if (await LoadFromStreamAsync(stream, replace))
                {
                    await SaveSettingsAsync(remote: false);
                    return true;
                }
            }
            catch (Exception ex)
            {
                ex.ReportCrash();
            }
        return false;
    }
    public static async Task<bool> LoadFromLocal()
    {
        Stream stream = new FileStream(VenueFullName, FileMode.Open, FileAccess.Read);
        if (stream is null)
            return false;
        else
            try
            {
                DateTime savedUpdateTime = App.Settings.VenueUpdateTime;
                if (savedUpdateTime == DateTime.MinValue)
                    savedUpdateTime = File.GetCreationTime(VenueFullName);
                await LoadFromStreamAsync(stream, true);
                //The deserialize operation changes the update time, so restore the old one
                UpdateTime = savedUpdateTime;
                // Record the update time to make up for a possible bad stored one, and because
                // we know the file is already in local storage so we don't need to archive it
                App.Settings.VenueUpdateTime = UpdateTime;
                return true;
            }
            catch (Exception ex)
            {
                ex.ReportCrash();
            }
        return false;
    }
    public static void InitializeFolders() => Directory.CreateDirectory(Path.GetDirectoryName(VenueFullName));
    public static async Task SaveSettingsAsync(bool remote = true)
    {
        using MemoryStream stream = new(10000);
        SerializeVenues(stream);
        Utilities.DebugExamineStream(stream);
        // Initiate local backup if it is permitted
        bool failed = true;
        Directory.CreateDirectory(Path.GetDirectoryName(VenueFullName));
        try
        {
            using (Stream fileStream = new FileStream(VenueFullName, FileMode.Create, FileAccess.Write))
            {
                stream.Position = 0;
                await stream.CopyToAsync(fileStream);
            }
            App.Settings.VenueUpdateTime = UpdateTime;
            App.Settings.VenueUpdater = Updater;
            failed = false;
        }
        catch (IOException ex)
        {
            Debug.WriteLine($">>>>> In Venue.{nameof(SaveSettingsAsync)}, exception {ex}");
            // Put it in the output stream, but just go on
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
        }
        if (failed)
            File.Delete(VenueFullName);
        // Initiate backup to cloud if it is permitted, do not wait for result
        if (remote && App.IsCloudAllowed)
        {
            stream.Position = 0;
            bool worked = await RemoteWs.PutItemStreamAsync(RemoteWs.VenueListTypeName, stream);
            if (worked && App.Settings.VenueUpdateTime < UpdateTime) // This update has not been noted yet, so do so
            {
                App.Settings.VenueUpdateTime = UpdateTime;
                App.Settings.VenueUpdater = Updater;
            }
        }
        // end using stream
    }

    public static List<Venue> DeserializeList(Stream stream)
    {
        try
        {
            var storedVenues = (VenueRoot)allVenuesSerializer.Deserialize(stream);
            return storedVenues.Venues;
        }
        catch (Exception ex)
        {
            Utilities.DebugMsg("In Venue.DeserializeList exception thrown:" + ex);
            return null;
        }
    }

    /// <summary>
    /// Merge a venue list with the current list or replace the current list with a new one.
    /// The new list is sorted in alphabetical order by name.
    /// </summary>
    /// <param name="sourceStream">The persisted XML describing the Venue list</param>
    /// <param name="replace">Whether to replace the old list completely or just merge in the new one</param>
    public static void MergeVenues(Stream sourceStream, bool replace)
    {
        List<Venue> storedVenues = DeserializeList(sourceStream);
        MergeVenues(storedVenues, replace);
    }
    public static void MergeVenues(List<Venue> newVenues, bool replace)
    {
        SortedDictionary<string, Venue> allVenuesDictionary = [];
        if (!replace)
            foreach (Venue r in allVenues)
                allVenuesDictionary.Add(r.Name, r);
        allVenues.Clear();
        allVenuesByDistance.Clear();

        foreach (Venue storedVenue in newVenues)
        {
            if (!string.IsNullOrEmpty(storedVenue.Name))
            {
                if (allVenuesDictionary.TryGetValue(storedVenue.Name, out Venue localVenue))
                {
                    if (localVenue.Accuracy > storedVenue.Accuracy)
                    {  // New Venue has a more accurate location, use it (big numbers are less accurate)
                        localVenue.Accuracy = storedVenue.Accuracy;
                        localVenue.Latitude = storedVenue.Latitude;
                        localVenue.Longitude = storedVenue.Longitude;
                    }
                    if (string.IsNullOrWhiteSpace(storedVenue.Notes))
                    { } // No need to do anything, the old notes (if any) are all there is
                    else if (string.IsNullOrWhiteSpace(localVenue.Notes) || !localVenue.Notes.Equals(storedVenue.Notes))
                        localVenue.Notes += storedVenue.Notes;
                    else
                        localVenue.Notes = storedVenue.Notes;
                }
                else
                    allVenuesDictionary.Add(storedVenue.Name, storedVenue);
            }
        }
        // allVenuesDictionary is now fully populated with what will become the new list so populate AllVenues with it
        foreach (KeyValuePair<string, Venue> keyValuePair in allVenuesDictionary)
        {
            Venue venue = keyValuePair.Value;
            venue.IsLocationValid = App.UseLocation && venue.Accuracy <= Distances.AccuracyLimit;
            allVenues.Add(venue); // Because allVenuesDictionary is a SortedDictionary it delivers in alphabetic order so we simply add it at the end of this list
        }
        // Now we deal with the list of venues by location by sorting the AllVenues list by location and then adding them in order to AllVenuesByDistance 
        // Future Distance updates will cause the venue to be relocated to the correct spot in the list
        List<Venue> listByDistance = [.. allVenues.ToList()];
        listByDistance.Sort((v1, v2) => v1.CompareDistanceTo(v2));
        foreach (Venue v in listByDistance)
            allVenuesByDistance.Add(v);
    }

    public bool InsertInVenueLists() => InsertInAllVenues() && InsertInAllVenuesByDistance();

    /// <summary>
    /// Insert a venue in AllVenues list in the correct place (as long as it is not a duplicate)
    /// </summary>
    /// <returns>true if inserted, false if not (because it was a duplicate)</returns>
    private bool InsertInAllVenues()
    {
        int index = -1, newIndex = -1;
        foreach (Venue item in allVenues)
        {
            index++;
            int i = CompareTo(item);
            if (i == 0)
                return false;
            else if (i < 0)
            {
                newIndex = index;
                break;
            }
        }
        if (newIndex < 0)
            allVenues.Add(this); // Item should go at end
        else
            allVenues.Insert(newIndex, this);
        return true;
    }

    /// <summary>
    /// Insert a venue in AllVenues list in the correct place (as long as it is not a duplicate)
    /// </summary>
    /// <returns>true if inserted, false if not (because it was a duplicate)</returns>
    private bool InsertInAllVenuesByDistance()
    {
        if (!allVenuesByDistanceIsSorted)
            return false;

        // Use binary search to find the correct insertion point
        int low = 0;
        int high = allVenuesByDistance.Count - 1;

        while (low <= high)
        {
            int mid = (low + high) >> 1;
            int comparison = CompareDistanceTo(allVenuesByDistance[mid]);

            if (comparison == 0)
                return false; // Duplicate found

            if (comparison < 0)
                high = mid - 1;
            else
                low = mid + 1;
        }

        allVenuesByDistance.Insert(low, this);
        return true;
    }

    /// <summary>
    /// Used to re-sort the whole list by distance because the location has changed
    /// </summary>
    /// <returns></returns>
    public static async Task UpdateAllDistances()
    {
        allVenuesByDistanceIsSorted = false;
        foreach (Venue v in allVenuesByDistance)
            v.Distance = Distances.Simplified(App.GetDistanceTo(v.Location));
        await Task.Yield();
        List<Venue> sortableList = [.. allVenuesByDistance];
        sortableList.Sort(CompareDistances);
        allVenuesByDistance.Clear();
        foreach (Venue v in sortableList)
            allVenuesByDistance.Add(v);
        allVenuesByDistanceIsSorted = true;
    }

    /// <summary>
    /// Returns the venue with the given name (creates one if there isn't one already)
    /// </summary>
    /// <param name="VenueName">Name of the venue to be selected (or created)</param>
    /// <param name="notesParam">Optional notes for a venue if one is created</param>
    /// <returns>Reference to the venue with the specified name</returns>
    public static Venue SelectOrAddVenue(string VenueName = null, string notesParam = null)
    {
        if (allVenues is null) // initializing
            return null;
        Venue v = new() { Name = VenueName ?? "New", Notes = notesParam };
        if (VenueName is null)
            v.Location = App.MyLocation; // Assign current location only to a newly created venue with no name
        // Find out where in the sorted list this venue should go, can't use BinarySearch method because it is
        // not defined for ObserveableList<T>
        int low = 0;
        int high = allVenues.Count - 1;

        while (low <= high)
        {
            int mid = (low + high) >> 1;
            int comparison = v.CompareTo(allVenues[mid]);

            if (comparison == 0)
                return allVenues[mid];

            if (comparison < 0)
                high = mid - 1;
            else
                low = mid + 1;
        }
        // If we get to here it was not found in AllVenues
        allVenues.Insert(low, v);
        v.InsertInAllVenuesByDistance();
        return v;
    }

    public static Venue FindVenueByName(string desiredName)
    {
        if (string.IsNullOrWhiteSpace(desiredName) || !(allVenues?.Count > 0)) // the current venue has been renamed or we're initializing or there just aren't any venues
            return null;
        Venue v = allVenues.Where(v1 => v1.Name.Equals(desiredName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        return v;
    }
    public static void SetCurrentByName(string desiredName) => Current = FindVenueByName(desiredName);
    public int CompareTo(Venue otherVenue) => string.Compare(Name, otherVenue.Name, ignoreCase: true);
    public static int CompareDistances(Venue item1, Venue item2) => item1.CompareDistanceTo(item2);
    public int CompareDistanceTo(Venue otherVenue)
    {
        if (this == otherVenue)
            return 0;
        if (otherVenue is null)
            return 1;
        int result = SimplifiedDistance.CompareTo(otherVenue.SimplifiedDistance);
        if (result == 0)
            result = CompareTo(otherVenue);
        return result;
    }

    public static void SerializeVenues(Stream s)
    {
        List<Venue> venues = [.. allVenues];
        venues.Sort((r1, r2) => r1.Name.CompareTo(r2.Name));
        VenueRoot vr = new() { Venues = venues };
        using (StreamWriter sw = new(s, System.Text.Encoding.UTF8, -1, true))
        using (var xmlWriter = XmlWriter.Create(sw, new XmlWriterSettings() { Indent = true, OmitXmlDeclaration = true }))
        {
            XmlSerializerNamespaces namespaces = new();
            namespaces.Add(string.Empty, string.Empty);
            allVenuesSerializer.Serialize(xmlWriter, vr, namespaces);
        }
        Utilities.DebugExamineStream(s);
    }

    public static ObservableCollection<Venue> AllVenues { get; } = allVenues;
    public static ObservableCollection<Venue> AllVenuesByDistance { get; } = allVenuesByDistance;

    internal static DateTime UpdateTime { get; set; }

    internal static bool IsSaved => App.Settings.VenueUpdateTime == UpdateTime;
    public static void MarkSaved() => UpdateTime = App.Settings.VenueUpdateTime;
    internal static bool IsDefaultList => UpdateTime == DateTime.MinValue;

    public static Guid Updater { get; set; }
    
    /// <summary>
    /// Use a new location if it is more accurate than the old one and within the same area
    /// </summary>
    /// <param name="newLocation">The candidate new location</param>
    public void SetLocationIfBetter(Location newLocation)
    {
        bool useNewLocation;
        if (newLocation.IsValid())
        {   // We have a pretty accurate location for this venue, so perhaps it's better
            if (IsLocationValid) // We currently have a location, so decide if the new one is better
            {
                double distanceBetweenVenues = newLocation.GetDistanceTo(Location);
                bool newLocationIsClose = distanceBetweenVenues < newLocation.Accuracy || distanceBetweenVenues < Accuracy;
                bool newLocationIsMoreAccurate = newLocation.AccuracyOrDefault() < Accuracy;
                useNewLocation = newLocationIsClose && newLocationIsMoreAccurate;
            }
            else
                useNewLocation = true;
            if (useNewLocation)
            {
                Location = newLocation;
                _ = Venue.SaveSettingsAsync();
            }
        }
    }

    [XmlIgnore]
    public Location Location
    {
        get => (Latitude == 0.0 && Longitude == 0.0) || !IsLocationValid
                ? MiddleOfNowhere
                : new Location(Latitude, Longitude) { Accuracy = Accuracy };
        set
        {
            if (value is null || !App.UseLocation || Accuracy is <= 0 or >= Distances.AccuracyLimit)
            {
                IsLocationValid = false;
                UpdateTime = DateTime.Now;
            }
            else if (Latitude != value.Latitude || Longitude != value.Longitude || Accuracy != value.AccuracyOrDefault())
            {
                Latitude = value.Latitude;
                Longitude = value.Longitude;
                Accuracy = value.AccuracyOrDefault();
                IsLocationValid = true;
                Distance = App.GetDistanceTo(Location);
                UpdateTime = DateTime.Now;
            }
            // else the location has not changed, do nothing
        }
    }

    [ObservableProperty]
    [XmlIgnore]
    public partial int Distance { get; set; } = Distances.Inaccurate;

    partial void OnDistanceChanged(int oldValue, int newValue)
    {
        if (allVenuesByDistanceIsSorted & allVenuesByDistance.Contains(this))
            MoveToCorrectPlaceByDistance();
        DistanceChanged?.Invoke(this, new VenueDistanceChangedEventArgs(this, oldValue, newValue));
        UpdateTime = DateTime.Now;
    }

    [XmlIgnore]
    public int SimplifiedDistance => Distances.Simplified(Distance);

    [ObservableProperty]
    [XmlAttribute]
    public partial string Name { get; set; }

    partial void OnNameChanged(string oldValue, string newValue)
    {
        Name = string.IsNullOrEmpty(newValue) ? null : newValue.Trim();
        if (allVenues.Contains(this))
            MoveToCorrectPlace();
        UpdateTime = DateTime.Now;
    }

    [ObservableProperty]
    [XmlAttribute]
    public partial string Notes { get; set; }

    partial void OnNotesChanged(string oldValue, string newValue)
    {
        Notes = string.IsNullOrEmpty(newValue) ? null : newValue.Trim();
        UpdateTime = DateTime.Now;
    }

    [ObservableProperty]
    [XmlIgnore]
    public partial bool IsForCurrentMeal { get; set; }

    [XmlAttribute(AttributeName = "Latitude"), DefaultValue(0.0)]
    public double AdjustedLatitude
    {
        set => Latitude = value;
        get => Utilities.Adjusted(Latitude, Accuracy);
    }

    [XmlIgnore]
    private double Latitude { get; set; } = 0.0;

    [XmlAttribute(AttributeName = "Longitude"), DefaultValue(0.0)]
    public double AdjustedLongitude
    {
        set => Longitude = value;
        get => Utilities.Adjusted(Longitude, Accuracy);
    }

    private double Longitude { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets a value indicating whether the current location meets the required validation criteria.
    /// </summary>
    /// <remarks>Use this property to determine if the location information provided is considered valid
    /// according to the application's validation rules. This property is typically used to enable or disable actions
    /// that require a valid location.</remarks>
    [ObservableProperty]
    [XmlIgnore]
    public partial bool IsLocationValid { get; private set; }
    partial void OnIsLocationValidChanged(bool value)
    {
        if (value)
        {
            if (App.UseLocation && Accuracy <= Distances.AccuracyLimit)
                Distance = App.GetDistanceTo(Location);
            else
                IsLocationValid = false;
        }
        else
        {
            // Reset these because they are persisted
            Latitude = 0.0;
            Longitude = 0.0;
            Accuracy = 0;
            // Reset distance because it is no longer correct
            Distance = Distances.Unknown;
        }
    }


    [ObservableProperty]
    [XmlAttribute, DefaultValue(0)]
    public partial int Accuracy { get; set; } = 0;

    partial void OnAccuracyChanged(int oldValue, int newValue)
    {
        if (newValue >= 0)
            Accuracy = newValue is > 0 and < Distances.Inaccurate ? newValue : 0;
        IsLocationValid = App.UseLocation && Accuracy is > 0 and <= Distances.AccuracyLimit;
    }
    public bool Forget() => allVenues.Remove(this) && allVenuesByDistance.Remove(this);
    public static void ForgetAllVenues()
    {
        Current = null; // Clear the current venue so it doesn't point to a deleted one
        allVenues.Clear();
        allVenuesByDistance.Clear();
    }
    private void MoveToCorrectPlace() => allVenues.Upsert(this);
    private void MoveToCorrectPlaceByDistance() => allVenuesByDistance.Upsert(this, CompareDistances);
}

// The VenueRoot class is needed because 'Venue' used to be 'Restaurant' and so the persisted XML has to use
// the old names, not the new ones so as to be able to import existing Venue,xml files.
[XmlRoot("ArrayOfRestaurant")]
public class VenueRoot
{
    public VenueRoot() => Venues = [];

    [XmlElement("Restaurant")]
    public List<Venue> Venues { get; set; }
}
public class VenueDistanceChangedEventArgs(Venue venue, int oldDistance, int newDistance) : EventArgs
{
    public Venue Venue { get; } = venue;
    public int OldDistance { get; } = oldDistance;
    public int NewDistance { get; } = newDistance;
}
