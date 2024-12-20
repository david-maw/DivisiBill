// Ignore Spelling: Deserialize

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Xml.Serialization;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using DivisiBill.Services;
using System.Xml;

namespace DivisiBill.Models;

[DebuggerDisplay("{Name}")]
public class Venue : INotifyPropertyChanged, IComparable<Venue>
{
    public const string VenueFolderName = "Venues";
    public const string TargetFileName = "Venues.xml";
    private static string TargetPathName = null;
    private readonly Location MiddleOfNowhere = new Location(20, 170 ); // Middle of the Pacific, not close to anything

    private static readonly ObservableCollection<Venue> allVenues = new ObservableCollection<Venue>();
    private static readonly ObservableCollection<Venue> allVenuesByDistance = new ObservableCollection<Venue>();
    private static bool allVenuesByDistanceIsSorted = true;
    static public Venue Current = null;

    static void LoadDefaultVenues()
    {
        var initialVenues = new List<Venue>() {
            new Venue() {Name = "California Pizza Kitchen", Latitude= 33.6120, Longitude = -117.7080, Accuracy = 10},
            new Venue() {Name = "Claim Jumper"},
            new Venue() {Name = "Kings",                    Latitude= 33.6132, Longitude = -117.7084, Accuracy = 10},
            new Venue() {Name = "MacDonalds"},
            new Venue() {Name = "Queasy Diner",             Latitude= 20.79, Longitude = -156.24, Accuracy = 700},
            new Venue() {Name = "Wendys"},
        };
        initialVenues.Sort();
        foreach (Venue v in initialVenues)
            allVenues.Add(v);
        initialVenues.Sort((Venue v1, Venue v2) => v1.CompareDistanceTo(v2));
        foreach (Venue v in initialVenues)
            allVenuesByDistance.Add(v);
        MarkSaved(); // Flag this as not needing to be saved so it won't be unless someone changes it 
    }
    public static async Task InitializeAsync(string BasePathName)
    {
        TargetPathName = Path.Combine(BasePathName, VenueFolderName, TargetFileName);
        await Task.Run(() => LoadSettings());
    }

    private static readonly XmlSerializer allVenuesSerializer = new XmlSerializer(typeof(VenueRoot));
    private static async Task<bool> LoadFromStreamAsync(Stream stream, bool replace)
    {
        if (stream is null)
            return false;
        else
            try
            {
                Updater = App.Settings.VenueUpdater;
                if (Updater == Guid.Empty)
                    Updater = App.Current.Id; // Set the current appid
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
        Stream stream = new FileStream(TargetPathName, FileMode.Open, FileAccess.Read);
        if (stream is null)
            return false;
        else
            try
            {
                DateTime savedUpdateTime = App.Settings.VenueUpdateTime;
                if (savedUpdateTime == DateTime.MinValue)
                    savedUpdateTime = File.GetCreationTime(TargetPathName);
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
    private static async Task LoadSettings()
    {
        bool loaded = false;
        if (File.Exists(TargetPathName))
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

    public static async Task SaveSettingsAsync(bool remote = true)
    {
        using (MemoryStream stream = new MemoryStream(10000))
        {
            SerializeVenues(stream);
            Utilities.DebugExamineStream(stream);
            // Initiate local backup if it is permitted
            bool failed = true;
            Directory.CreateDirectory(Path.GetDirectoryName(TargetPathName));
            try
            {
                using (Stream sfile = new FileStream(TargetPathName, FileMode.Create, FileAccess.Write))
                {
                    stream.Position = 0;
                    await stream.CopyToAsync(sfile);
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
                File.Delete(TargetPathName);
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
        } // end using stream
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
        var storedVenues = DeserializeList(sourceStream);
        MergeVenues(storedVenues, replace);
    }
    public static void MergeVenues(List<Venue> newVenues, bool replace)
    {
        var allVenuesDictionary = new SortedDictionary<String, Venue>();
        if (!replace)
            foreach (var r in allVenues)
                allVenuesDictionary.Add(r.Name, r);
        allVenues.Clear();
        allVenuesByDistance.Clear();

        foreach (var storedVenue in newVenues)
        {
            if (!string.IsNullOrEmpty(storedVenue.Name))
            {
                if (allVenuesDictionary.ContainsKey(storedVenue.Name))
                {
                    // We are loading a duplicate name
                    Venue localVenue = allVenuesDictionary[storedVenue.Name];
                    if (localVenue.Accuracy > storedVenue.Accuracy)
                    {  // New Venue has a more accurate location, use it (big numbers are less accurate)
                        localVenue.Accuracy = storedVenue.Accuracy;
                        localVenue.Latitude = storedVenue.Latitude;
                        localVenue.Longitude = storedVenue.Longitude;
                    }
                    if (string.IsNullOrWhiteSpace(storedVenue.Notes))
                    { } // No need to do anything, the old notes (if any) ares all there is
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
        foreach (var keyValuePair in allVenuesDictionary) 
        {
            Venue venue = keyValuePair.Value;
            venue.IsLocationValid = App.UseLocation && venue.Accuracy <= Distances.AccuracyLimit;
            allVenues.Add(venue); // Because allVenuesDictionary is a SortedDictionary it delivers in alphabetic order so we simply add it at the end of this list
        }
        // Now we deal with the list of venues by location by sorting the AllVenues list by location and then adding them in order to AllVenuesByDistance 
        // Future Distance updates will cause the venue to be relocated to the correct spot in the list
        List<Venue> listByDistance = new List<Venue>(allVenues.ToList());
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
        foreach (var item in allVenues)
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
        int index = -1, newIndex = -1;
        if (allVenuesByDistanceIsSorted) foreach (var item in allVenuesByDistance)
        {
            index++;
            int i = CompareDistanceTo(item);
            if (i == 0)
                return false;
            else if (i < 0)
            {
                newIndex = index;
                break;
            }
        }
        if (newIndex < 0)
            allVenuesByDistance.Add(this); // Item should go at end
        else
            allVenuesByDistance.Insert(newIndex, this);
        return true;
    }

    /// <summary>
    /// Used to re-sort the whole list by distance because the location has changed
    /// </summary>
    /// <returns></returns>
    public async static Task UpdateAllDistances()
    {
        allVenuesByDistanceIsSorted = false;
        foreach (var v in allVenuesByDistance)
            v.Distance = Distances.Simplified(App.Current.GetDistanceTo(v.Location));
        await Task.Yield();
        var sortableList = new List<Venue>(allVenuesByDistance);
        sortableList.Sort(CompareDistances);
        allVenuesByDistance.Clear();
        foreach (var v in sortableList)
            allVenuesByDistance.Add(v);
        allVenuesByDistanceIsSorted = true;
    }

    /// <summary>
    /// Returns the venue with the given name (creates one if there isn't one already)
    /// </summary>
    /// <param name="VenueName">Name of the venue to be selected (or created)</param>
    /// <param name="notesParam">Optional notes for a venue if one is created</param>
    /// <returns>Reference to the venue with the specified name</returns>
    public static Venue SelectOrAddVenue(string VenueName, string notesParam = null)
    {
        Venue v = new Venue() { name = VenueName, Notes = notesParam };
        if (allVenues is null) // initializing
            return null;
        int index = -1, newIndex = -1;
        foreach (var item in allVenues)
        {
            index++;
            int i = v.CompareTo(item);
            if (i == 0)
                return item;
            else if (i < 0)
            {
                newIndex = index;
                break;
            }
        }
        // If we get to here it was not found in AllVenues
        v.Location = App.MyLocation;
        if (newIndex < 0)
            allVenues.Add(v); // Item should go at end
        else
            allVenues.Insert(newIndex, v);
        v.InsertInAllVenuesByDistance();
        return v;
    }

    public static Venue FindVenueByName(string desiredName)
    {
        if (!(allVenues?.Count > 0)) // initializing or there just aren't any
            return null;
        Venue v = allVenues.Where(v1 => v1.Name.Equals(desiredName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        return v;
    }
    public int CompareTo(Venue otherVenue) => string.Compare(this.Name, otherVenue.Name, ignoreCase: true);
    public static int CompareDistances(Venue item1, Venue item2) => item1.CompareDistanceTo(item2);
    public int CompareDistanceTo(Venue otherVenue)
    {
        if (this == otherVenue) return 0;
        if (otherVenue is null) return 1;
        int result = SimplifiedDistance.CompareTo(otherVenue.SimplifiedDistance);
        if (result == 0)
            result = CompareTo(otherVenue);
        return result;
    }

    public static void SerializeVenues(Stream s)
    {
        var venues = new List<Venue>(allVenues);
        venues.Sort((r1, r2) => r1.Name.CompareTo(r2.Name));
        VenueRoot vr = new VenueRoot() { Venues = venues };
        using (StreamWriter sw = new(s, System.Text.Encoding.UTF8, -1, true))
        using (var xmlwriter = XmlWriter.Create(sw, new XmlWriterSettings() { Indent = true, OmitXmlDeclaration = true}))
        {
            var namespaces = new XmlSerializerNamespaces();
            namespaces.Add(string.Empty, string.Empty);
            allVenuesSerializer.Serialize(xmlwriter, vr, namespaces);
        }
        Utilities.DebugExamineStream(s);
    }

    public static ObservableCollection<Venue> AllVenues { get; } = allVenues;
    public static ObservableCollection<Venue> AllVenuesByDistance { get; } = allVenuesByDistance;

    private static DateTime updateTime;

    public static DateTime UpdateTime
    {
        get => updateTime;
        set => updateTime = value;
    }

    public static bool IsSaved => App.Settings.VenueUpdateTime == UpdateTime;
    public static void MarkSaved() => UpdateTime = App.Settings.VenueUpdateTime;
    public static bool IsDefaultList => UpdateTime == DateTime.MinValue;

    private static Guid updater;

    public static Guid Updater
    {
        get => updater;
        set => updater = value;
    }
    /// <summary>
    /// Use a new location if it is more accurate than the old one and within the same area
    /// </summary>
    /// <param name="newLocation">The candidate new location</param>
    public void SetLocationIfBetter(Location newLocation)
    {
        bool useNewLocation = false;
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

    public event PropertyChangedEventHandler PropertyChanged;

    [XmlIgnore]
    public Location Location
    {
        get
        {
            if ((Latitude == 0.0 && Longitude == 0.0) || !IsLocationValid)
                return MiddleOfNowhere;
            else
                return new Location(this.Latitude, this.Longitude) { Accuracy = Accuracy };
        }
        set
        {
            if (value is null)
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
                Distance = App.Current.GetDistanceTo(Location);
                UpdateTime = DateTime.Now;
            }
            // else the location has not changed, do nothing
        }
    }

    private int distance = Distances.Inaccurate;

    [XmlIgnore]
    public int Distance
    {
        set 
        {
            if ((Latitude == 0.0 && Longitude == 0.0) || !IsLocationValid)
            {
                value = Distances.Inaccurate;
                //if (Utilities.IsDebug)
                //    throw new ArgumentException("Attempt to set Distance to an invalid Venue Location");
            }
            if (distance != value)
            {
                distance = value;
                if (allVenuesByDistanceIsSorted & allVenuesByDistance.Contains(this))
                    MoveToCorrectPlaceByDistance();
                OnPropertyChanged();
            }
        }
        get => distance;
    }
    [XmlIgnore]
    public int SimplifiedDistance => Distances.Simplified(distance);

    string name;

    [XmlAttribute]
    public string Name
    {
        set
        {
            string newValue = null;
            if (!string.IsNullOrEmpty(value))
                newValue = value.Trim();
            if (name != newValue)
            {
                name = newValue;
                if (allVenues.Contains(this)) MoveToCorrectPlace();
                OnPropertyChanged();
                UpdateTime = DateTime.Now;
            }
        }
        get => name;
    }

    string notes;

    [XmlAttribute]
    public string Notes
    {
        set
        {
            string newValue = null;
            if (!string.IsNullOrEmpty(value))
                newValue = value.Trim();
            if (notes != newValue)
            {
                notes = newValue;
                OnPropertyChanged();
            }
        }
        get => notes;
    }

    public bool IsCurrentMeal => string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Meal.CurrentMeal.VenueName) ? false : Name == Meal.CurrentMeal.VenueName;

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

    private bool isLocationValid;

    [XmlIgnore]
    public bool IsLocationValid
    {
        get => App.UseLocation && Accuracy <= Distances.AccuracyLimit && isLocationValid;
        set
        {
            if (value != isLocationValid)
            {
                isLocationValid = value;
                if (value)
                    Distance = App.Current.GetDistanceTo(Location);
                else
                {
                    // Reset these because they are persisted
                    Latitude = 0.0;
                    Longitude = 0.0;
                    Accuracy = 0;
                    // Reset distance because it is no longer correct
                    Distance = Distances.Unknown;
                }
                OnPropertyChanged();
            }
        }
    }

    private int accuracy;

    [XmlAttribute, DefaultValue(0)]
    public int Accuracy
    {
        set
        {
            if (value>=0 && accuracy != value)
            {
                accuracy = value <= 0 || value >= Distances.Inaccurate ? 0 : value;
                IsLocationValid = (accuracy != 0);
            }
        }

        get
        {
            if (accuracy > 0)
                return accuracy;
            else
            {
                if (Latitude == 0 && Longitude == 0)
                    return Distances.Inaccurate;
                else
                    return Distances.AccuracyLimit;
            }
        }
    }
    public bool Forget() => allVenues.Remove(this) && allVenuesByDistance.Remove(this);
    public static void ForgetAllVenues()
    {
        allVenues.Clear();
        allVenuesByDistance.Clear();
    }
    private void MoveToCorrectPlace() => allVenues.Upsert(this);
    private void MoveToCorrectPlaceByDistance() => allVenuesByDistance.Upsert(this, CompareDistances);

    protected virtual void OnPropertyChanged([CallerMemberName] string propChanged = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propChanged));
    }
}

// The VenueRoot class is needed because 'Venue' used to be 'Restaurant' and so the persisted XML has to use
// the old names, not the new ones so as to be able to import existing Venue,xml files.
[XmlRoot("ArrayOfRestaurant")]
public class VenueRoot
{
    public VenueRoot() { this.Venues = new List<Venue>(); }

    [XmlElement("Restaurant")]
    public List<Venue> Venues { get; set; }
}
