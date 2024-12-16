using System.ComponentModel;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Collections.ObjectModel;
using DivisiBill.Services;
using static DivisiBill.Services.Utilities;
using System.Xml.Serialization;
using System.Diagnostics;
using System.Xml;

namespace DivisiBill.Models;

/// <summary>
/// Defines the summary of each Meal, and can be created either from the XML description of a Meal 
/// (which is why the XmlRoot name is "Meal" not "MealSummary" or from the description attached to 
/// a file in Azure (a JSON encoded MealSummary object). 
/// </summary>
[DebuggerDisplay("{DebugDisplay}")]
[DataContract]
[XmlRoot(ElementName = "Meal")]
public class MealSummary : ObservableObjectPlus, IComparable<MealSummary>
{
    #region Global
    public MealSummary() { }

    public MealSummary ShallowCopy() => MemberwiseClone() as MealSummary;

    public override bool Equals(object obj)
    {
        if (obj is null)
            return false;
        if (ReferenceEquals(this, obj))
            return true;
        if (this.GetType() != obj.GetType())
            return false;
        return FileName == ((MealSummary)obj).FileName;
    }
    public override int GetHashCode() => FileName.GetHashCode();
    public override string ToString() => FileName + " (" + VenueName + ")";
    #endregion
    #region Persisted Data
    [DataMember(Order = 9, EmitDefaultValue = false)]
    [DefaultValue(0)]
    public int StoredVersion { get => mealSummaryVersion; set => mealSummaryVersion = value; }
    private int mealSummaryVersion = 2;

    [DataMember(Name = "Restaurant", Order = 1)]
    [XmlElement(ElementName = "Restaurant")]
    public string VenueName
    {
        get => venueName;
        set
        {
            if (value is null) 
                throw new ArgumentNullException("value");
            SetProperty(ref venueName, value);
        }
    }

    /// <summary>
    /// The curious layout of the xxxTime and ActualxxxTime properties is because we want to store the times accurately
    /// with time zone information but show them to the human as if they were all local times, so dinner in Mumbai and Dinner 
    /// in California both show as happening in the evening. Most people only ever operate in a single time zone, but for those
    /// that do not this seems like the least bad choice. More importantly, it means that the file name and the creationtime
    /// align regardless of timezone.
    /// 
    /// The definitions for MealSummary are a bit more complex than for Meal because MealSummaries are deserialized from XML 
    /// and JSON (they are only ever serialized to JSON) whereas Meal objects are only ever serialized and deserialized from XML.
    /// </summary>

    [DataMember(Name = "LastChangeTime", EmitDefaultValue = false)]
    [XmlElement(ElementName = "LastChangeTime")]
    public string StoredLastChangeTime
    {
        get
        {
            if (ActualLastChangeTime.DateTime == DateTime.MinValue)
                return null;
            else
                return ActualLastChangeTime.ToString("s", System.Globalization.CultureInfo.InvariantCulture) + ActualLastChangeTime.ToString("zzz", System.Globalization.CultureInfo.InvariantCulture);
        }

        set
        {
            DateTimeOffset result;
            if (!DateTimeOffset.TryParse(value, out result) && !TryParseJsonDate(value, out result))
                result = DateTimeOffset.MinValue;
            ActualLastChangeTime = result;
        }
    }
    [XmlIgnore]
    public DateTimeOffset ActualLastChangeTime
    {
        get => lastChangeTime;
        set
        {
            TimeSpan sinceLastUpdate = TimeSpan.Zero;
            try
            {
                sinceLastUpdate = value.DateTime - lastChangeTime;
            }
            catch (Exception)
            {
                // Do nothing - value was probably illegal
            }
            if (sinceLastUpdate > TimeSpan.FromMilliseconds(50))
            {
                if (lastChangeTime != DateTime.MinValue)
                    Utilities.DebugMsg($"LastChangeTime updated, delta = {sinceLastUpdate}");
                lastChangeTime = lastChangeTime + sinceLastUpdate;
                OnPropertyChanged(nameof(LastChangeTime));
            }
        }
    }
    public string GetLastChangeString()
    {
        if (!WithinOneSecond(CreationTime, LastChangeTime))
        {
            TimeSpan untilUpdate = LastChangeTime - CreationTime;
            untilUpdate += TimeSpan.FromSeconds(1) - TimeSpan.FromTicks(untilUpdate.Ticks % TimeSpan.TicksPerSecond); // Round up to next second
            string ago;
            if (untilUpdate.TotalSeconds < 0) // Weird special case, updated before creation
                return LastChangeTime.ToString("g"); // Just display the date and time

            if (untilUpdate.TotalSeconds <= 119)
                ago = untilUpdate.TotalSeconds.ToString("F0") + " s";
            else if (untilUpdate.TotalMinutes <= 119)
                ago = untilUpdate.TotalMinutes.ToString("F0") + " min";
            else if (untilUpdate.TotalHours <= 47)
                ago = untilUpdate.TotalHours.ToString("F1") + " hours";
            else
                ago = untilUpdate.ToString("g");
            return ago + " later";
        }
        return null;
    }

    public bool IsForCurrentMeal => CreationTime == Meal.CurrentMeal.CreationTime;
    
    /// <summary>
    /// Last time a bill was changed unlike ActualLastChangeTime this defaults to creation time if not set
    /// </summary>
    public DateTime LastChangeTime => IsLastChangeTimeSet ? lastChangeTime : CreationTime;

    public bool IsLastChangeTimeSet => lastChangeTime > DateTime.MinValue;

    [DataMember(Name = "CreationTime")]
    [XmlElement(ElementName = "CreationTime")]
    public string StoredCreationTime
    {
        get => ActualCreationTime.ToString("s", System.Globalization.CultureInfo.InvariantCulture) + ActualCreationTime.ToString("zzz", System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            DateTimeOffset result;
            if (!DateTimeOffset.TryParse(value, out result) && !TryParseJsonDate(value, out result))
                result = DateTimeOffset.MinValue;
            ActualCreationTime = result;
        }
    }
    [XmlIgnore]
    public DateTimeOffset ActualCreationTime
    {
        get => creationTime;
        set
        {
            DateTime withoutTimezone = value.DateTime;
            if (withoutTimezone != creationTime)
            {
                creationTime = withoutTimezone;
                OnPropertyChanged(nameof(CreationTime));
            }
        }
    }
    [XmlIgnore]
    public DateTime CreationTime 
    { 
        get => creationTime;
        set => SetProperty(ref creationTime, value, () => 
        {
            OnPropertyChanged(nameof(ApproximateAge));
            OnPropertyChanged(nameof(Id));
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(ImagePath));
            OnPropertyChanged(nameof(DeletedImagePath));
            OnPropertyChanged(nameof(ImageName));
        }); 
    }
    public bool IsDefault => CreationTime == DateTime.MinValue;
    [DataMember]
    [XmlIgnore]
    public decimal RoundedAmount { get => roundedAmount; set => SetProperty(ref roundedAmount, value); }
    #endregion
    #region Member Data
    // This is just data stored with each object in memory, but not persisted when the object
    // is serialized
    public string DebugDisplay => "\"" + VenueName + "\"" + (CreationTime == DateTime.MinValue ? ", IsDefault" : $" at {CreationTime} {ApproximateAge} in {FileName}");
    public string DefaultId => NameFromDateTime(CreationTime);

    /// <summary>
    /// The last image stored to local storage (disk)
    /// </summary>
    [XmlIgnore]
    public Stream SnapshotStream { get; set; } = new MemoryStream(4000);

    /// <summary>
    /// The last image stored to local storage (disk)
    /// </summary>
    [XmlIgnore]
    public bool SnapshotValid => SnapshotStream is not null && SnapshotStream.Length > 0;


    public void CopySnapshotTo(Stream stream) => SnapshotStream.CopyTo(stream);
    
    /// <summary>
    /// The identity of the MealSummary - actually a string of date+time to the second
    /// </summary>
    public string Id => NameFromDateTime(CreationTime);
    
    /// <summary>
    /// The name of the file this MealSummary (or the Meal that points to it) is stored in, either
    /// in local storage, remote storage, or both. 
    /// </summary>
    [XmlIgnore]
    public string FileName => Id + ".xml";
    
    /// <summary>
    /// The full path to the local file this MealSummary (or the Meal that points to it) is stored in.
    /// </summary>
    public string FilePath => Path.Combine(Meal.MealFolderPath, FileName);
    /// <summary>
    /// The full path to the deleted file this MealSummary (or the Meal that points to it) is stored in.
    /// </summary>
    public string DeletedFilePath => Path.Combine(Meal.DeletedItemFolderPath, FileName);

    /// <summary>
    /// The (fixed) name of the stored image file 
    /// </summary>
    public string ImageName => Id + ".jpg";
    
    /// <summary>
    /// The fully qualified path to the bill image for this bill
    /// </summary>
    public string ImagePath => Path.Combine(Meal.ImageFolderPath, ImageName);
    /// <summary>
    /// The full path to the local file the deleted image for this bill is stored in.
    /// </summary>
    public string DeletedImagePath => Path.Combine(Meal.DeletedItemFolderPath, ImageName);
    public void SetCreationTimeFromFileName(string fn)
    {
         if (TryDateTimeFromName(fn, out DateTime dt))
            CreationTime = dt;
    }
    private bool isRemote = false;
    
    /// <summary>
    /// true if any version of the MealSummary file is in remote storage (it may not be the most current version, Meal.SavedToRemote will tell you that)
    /// </summary>
    [XmlIgnore]
    public bool IsRemote
    {
        get => isRemote;
        set => SetProperty(ref isRemote, value, () => OnPropertyChanged(nameof(IsFake)));
    }

    /// <summary>
    /// true if any version of the MealSummary file is in local storage  (it may have been updated since - Meal.SavedToFile will tell you that)
    /// </summary>
    [XmlIgnore]
    public bool IsLocal { get => isLocal; set => SetProperty(ref isLocal, value, () => OnPropertyChanged(nameof(IsFake))); }
    
    /// <summary>
    /// Means the meal is not stored locally or remotely
    /// </summary>
    [XmlIgnore]
    public bool IsFake => !(IsLocal || IsRemote);

    /// <summary>
    /// The last known ImageID in the cloud for this file, someone could delete the file and add another of the 
    /// same name then the ImageID would be stale
    /// </summary>
    [XmlIgnore]
    public String ImageID { get => imageID; set => SetProperty(ref imageID, value); }
    public String ApproximateAge => ApproximateAge(CreationTime);

    private static readonly Stack<MealSummary> deletedStack = new Stack<MealSummary>();
    public static Stack<MealSummary> DeletedStack => deletedStack;

    public static void ForgetDeleted()
    {
        DeletedStack.Clear();
        if (Directory.Exists(Meal.DeletedItemFolderPath))
            foreach (string fileName in Directory.GetFiles(Meal.DeletedItemFolderPath).Select(fp => Path.GetFileName(fp)))// everything in the folder
                File.Delete(fileName);
    }

    public void DeleteImage()
    {
        if (HasImage)
        {
            Directory.CreateDirectory(Meal.DeletedItemFolderPath);
            File.Move(ImagePath, DeletedImagePath, true);
            HasDeletedImage = true;
            HasImage = false;
        }
    }
    public bool ReplaceImage(string PathToNewImage)
    {
        if (File.Exists(PathToNewImage))
        {
            DeleteImage();
            File.Move(PathToNewImage, ImagePath);
            HasImage = false; // Toggle hasImage so as to trigger a refresh if needed
            return HasImage = true;
        }
        return false;
    }
    public bool DetermineHasImage() => HasImage = File.Exists(ImagePath);
    public bool DetermineHasDeletedImage() => HasDeletedImage = File.Exists(DeletedImagePath); 

    // Deletes all local copies of meals with no option for recovery (used with archive restore)
    // Does NOT delete any corresponding image so that restoring a meal will restore access to the corresponding image 
    public static void PermanentlyDeleteLocalMeals(DateOnly startDate, DateOnly finishDate)
    {
        var summaries = Meal.LocalMealList.ToList(); // The ToList is needed so we can mess with the list during the loop below
        summaries.Add(Meal.CurrentMeal.Summary); // We're deleting this too, it might not be in the list
        foreach (MealSummary ms in summaries.Where(ms => DateOnly.FromDateTime(ms.CreationTime) >= startDate && DateOnly.FromDateTime(ms.CreationTime) <= finishDate && ms.IsLocal))
        {
            ms.LocationChanged(isLocal: false);
            try
            {
                File.Delete(ms.FilePath);
            }
            catch (Exception ex)
            {
                ex.ReportCrash();
            }
        }
    }
    public async Task DeleteAsync(bool doLocal = true, bool doRemote = true)
    {
        if (doLocal && IsLocal)
        {
            Directory.CreateDirectory(Meal.DeletedItemFolderPath);
            DeleteImage();
            File.Move(FilePath, DeletedFilePath, true); // Overwrite any formerly deleted file of the same name
            DeletedStack.Push(this);
            LocationChanged(isLocal: false);
        }
        if (doRemote && IsRemote)
        {
            await RemoteWs.DeleteMealAsync(this);
            LocationChanged(isRemote: false);
        }
        if (IsFake) // neither local nor remote (so only in memory), nothing persistent so just forget it exists
            Meal.LocalMealList.Remove(this);
    }

    public static MealSummary PopMostRecentDeletion() => DeletedStack.Any() ? DeletedStack.Pop() : null;
    /// <summary>
    /// Undelete a local Meal and its associated image, if by some weird mischance the corresponding file already exists, just discard it
    /// </summary>
    public bool UnDelete()
    {
        bool unexpected = false;
        if (File.Exists(DeletedFilePath))
        {
            if (File.Exists(FilePath))
                unexpected = true;
            File.Move(DeletedFilePath, FilePath, true);
            LocationChanged(isLocal: true);
            TryUndeleteImage();
        }
        return unexpected;
    }
    /// <summary>
    /// Undelete a Meal image, if meal currently has an image, swap them
    /// </summary>
    public void TryUndeleteImage()
    {
        if (HasDeletedImage)
        {
            if (HasImage)
                File.Move(ImagePath, Meal.TempImageFilePath, true);
            File.Move(DeletedImagePath, ImagePath);
            if (HasImage)
                File.Move(Meal.TempImageFilePath, DeletedImagePath);
            HasDeletedImage = HasImage;
            HasImage = true;
        }
    }

    /// <summary>
    /// Adds the MealSummary to the local and remote lists if it should be there and isn't already
    /// </summary>
    public void Show()
    {
        if (IsLocal)
            TryAddTo(Meal.LocalMealList);
        if (IsRemote && Meal.RemoteMealList.Contains(this))
            TryAddTo(Meal.RemoteMealList);
    }

    /// <summary>
    /// Notify a change when a meal is added or deleted, primarily this updates the relevant MealList(s)
    /// </summary>
    /// <param name="isLocal">Whether the meal is now local, not local, or unchanged</param>
    /// <param name="isRemote">Whether the meal is now remote, not remote, or unchanged</param>
    /// <exception cref="Exception"></exception>
    public void LocationChanged(bool? isLocal = null, bool? isRemote = null)
    {
        if (isLocal is null && isRemote is null)
            throw new Exception("Program fault");
        if (isLocal is not null) // Then it changed
        {
            IsLocal = isLocal == true;
            if (IsLocal) // added
            {
                TryAddTo(Meal.LocalMealList);
                if (!IsRemote)
                    Meal.QueueForBackup(this); // Back it up if it wasn't already remote
            }
            else // Removed local copy
                Meal.LocalMealList.Remove(this);
        }
        if (isRemote is not null) // added to cloud
        {
            IsRemote = isRemote == true;
            if (isRemote == true)
                TryAddTo(Meal.RemoteMealList);
            else // removed from cloud
                Meal.RemoteMealList.Remove(this);
        }
    }

    private bool fileSelected;
    private string venueName = string.Empty;
    private DateTime creationTime = DateTime.MinValue;
    private DateTime lastChangeTime = DateTime.MinValue;
    private decimal roundedAmount;
    private bool isLocal = false;
    private string imageID;

    [XmlIgnore]
    public bool FileSelected
    {
        get => fileSelected;
        set => SetProperty(ref fileSelected, value);
    }

    [XmlIgnore]
    public long Size
    {
        get; set;
    }

    private bool hasImage = false;
    [XmlIgnore]
    public bool HasImage
    {
        get => hasImage;
        private set => SetProperty(ref hasImage, value);
    }

    private bool hasDeletedImage = false;
    [XmlIgnore]
    public bool HasDeletedImage
    {
        get => hasDeletedImage;
        private set => SetProperty(ref hasDeletedImage, value);
    }

    [XmlIgnore]
    public int Distance { get; set; } = Distances.Inaccurate;
    #endregion
    #region Persistence
    private static readonly DataContractJsonSerializer mealSummarySerializer = new DataContractJsonSerializer(typeof(MealSummary));
    private static readonly XmlSerializer mealSummaryXmlSerializer = new XmlSerializer(typeof(MealSummary));
    public static MealSummary LoadJsonFromStream(Stream sourceStream)
    {
        MealSummary ms = null;
        try
        {
            ms = (MealSummary)mealSummarySerializer.ReadObject(sourceStream);
            // Beware the MealSummary constructor is not called above use [OnDeserializing] or [OnDeserialized] if that's ever needed
            ms.HasImage = File.Exists(ms.ImagePath);
        }
        catch (ArgumentNullException)
        {
            // Probably a dubious JSON stream, just ignore it
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
        }
        return ms;
    }
    public string GetJsonString()
    {
        var buf = new byte[10000];
        MemoryStream s = new MemoryStream(buf);
        SaveJsonToStream(s);
        string myString = System.Text.Encoding.UTF8.GetString(buf, 0, (int)s.Position);
        return myString;
    }
    public void SaveJsonToStream(Stream s) => mealSummarySerializer.WriteObject(s, this);
    public static MealSummary LoadFromMealStream(Stream sourceStream, string diagnosticName)
    {
        MealSummary ms;
        DebugExamineStream(sourceStream);
        if (sourceStream.Length == 0)
        {
            if (Debugger.IsAttached)
                Debugger.Break();
            return null;
        }
        try
        {
            ms = (MealSummary)mealSummaryXmlSerializer.Deserialize(sourceStream);
            // Deserialize above calls the MealSummary constructor
            ms.Size = (int)sourceStream.Length;
            ms.HasImage = File.Exists(ms.ImagePath);
        }
        catch (Exception ex)
        {
            ex.ReportCrash("Meal Deserialize Faulted", sourceStream, diagnosticName);
            return null;
        }
        DebugExamineStream(sourceStream);
        return ms;
    }
            
    /// <summary>
    /// Create a MealSummary from a Meal stored in XML. Note that a MealSummary is never used to create
    /// an XML stream, we only ever reconstitute a Meal XML as a MealSummary object.
    /// </summary>
    /// <param name="TargetFileName"></param>
    /// <returns></returns>
    /// <exception cref="NullReferenceException"></exception>
    public static MealSummary LoadFromMealFile(string TargetFileName)
    {
        MealSummary ms = null;
        using (var sourceStream = File.OpenRead(Path.Combine(Meal.MealFolderPath, TargetFileName)))
        {
            LineItem.nextItemNumber = 1;
            if (sourceStream.Length > 0) // Empty files are clearly bad
                ms = LoadFromMealStream(sourceStream, TargetFileName);
            if (ms is null)
            {
                // The stream was bad so hide the file and return null
                sourceStream.Close(); // first close the file so it can be moved
                Utilities.DebugMsg($"In MealSummary.LoadFromMealFile(\"{TargetFileName}\") bad file detected");
                ms = new MealSummary() { VenueName = "Bad Bill Data - will hide", Size = -1 };
                ms.SetCreationTimeFromFileName(TargetFileName);
            }
            else
            {
                if (ms.CreationTime == DateTime.MinValue) // It's a file without a stored creation time
                    ms.SetCreationTimeFromFileName(TargetFileName);
                else if (ms.FileNameInconsistent(TargetFileName)) // verify that the name corresponds to the stored creation date
                {
                    ms.Size = -2; // flag it as suspect
                    ms.VenueName = "Suspect File - will hide";
                }
            }
        }
        if (ms.Size < 0)
        {
            Meal.MoveSuspectFile(TargetFileName);
        }
        else
        {
            ms.IsLocal = true;
        }
        return ms;
    }
    public bool FileNameInconsistent(string fn)
    {
        if (string.IsNullOrEmpty(fn))
            return false; // it's not set, so it's consistent by definition
        DateTime fileNameTime = DateTimeFromName(Path.GetFileNameWithoutExtension(fn));
        return !WithinOneSecond(fileNameTime, CreationTime);
    }
    public static async Task<MealSummary> LoadFromRemoteMealAsync(string name)
    {
        MealSummary resultMs = null;
        using (Stream sourceStream = await RemoteWs.GetItemStreamAsync(RemoteWs.MealTypeName, name))
        {
            resultMs = LoadFromMealStream(sourceStream, name);
            if (resultMs is null)
            {
                // The stream was bad so just return null
                Utilities.DebugMsg($"MealSummary.LoadFromRemoteMeal returning null for name = {name}");
            }
            else
                resultMs.IsRemote = true;
        }
        return resultMs;
    }
    #endregion
    #region Lists
    /// <summary>
    /// Add the current item to the specified list iff it is not already there. Assumes the list is in descending order by CreationTime
    /// </summary>
    /// <param name="list"></param>
    /// <returns></returns>
    public bool TryAddTo(Collection<MealSummary> list)
    {
        (var ms, var inx) = list.FindItemAndIndex((item) => item.CreationTime <= this.CreationTime);
        if (ms is null)
            list.Add(this);
        else if (ms.CreationTime == this.CreationTime)
            return false; // Nothing to do, it is already in the list
        else
            list.Insert(inx, this);
        return true; // Item added
    }
    public int CompareTo(MealSummary otherMs) => CompareCreationTimeTo(otherMs);

    /// <summary>
    /// Compare by creation time, latest first. No two creation times should be the same.
    /// </summary>
    /// <param name="otherMs">the MealSummary to compare the current one with</param>
    /// <returns>+1 if this is later than the parameter, 0 if they are the same (should not happen),-1 if this should precede the parameter</returns>
    public int CompareCreationTimeTo(MealSummary otherMs) => otherMs.CreationTime.CompareTo(CreationTime); // Note that this is inverted because we want newest first;
    public static int CompareCreationTimeTo(MealSummary thisMs, MealSummary otherMs) => thisMs.CompareCreationTimeTo(otherMs);

    /// <summary>
    /// Compare by venue name then creation time newest first
    /// </summary>
    /// <param name="otherMs">the MealSummary to compare the current one with</param>
    /// <returns>+1 if this would sort later than the parameter, 0 if they are the same (should not happen),-1 if this should precede the parameter</returns>
    public int CompareVenueTo(MealSummary otherMs)
    {
        if (this.Equals(otherMs)) return 0;
        if (otherMs is null) return 1;
        int result = VenueName.CompareTo(otherMs.VenueName);
        if (result == 0)
            result = CompareCreationTimeTo(otherMs);
        if (result == 0 && Debugger.IsAttached)
            Debugger.Break(); // let the developer know there's a problem
        return result;
    }
    public static int CompareVenueTo(MealSummary thisMs, MealSummary otherMs) => thisMs.CompareVenueTo(otherMs);

    /// <summary>
    /// Compare by distance then venue name then newest first
    /// </summary>
    /// <param name="otherMs">the MealSummary to compare the current one with</param>
    /// <returns>+1 if this would sort later than the parameter, 0 if they are the same (should not happen),-1 if this should precede the parameter</returns>
    public int CompareDistanceTo(MealSummary otherMs)
    {
        if (this.Equals(otherMs)) return 0;
        if (otherMs is null) return 1;
        int result = Distance.CompareTo(otherMs.Distance);
        if (result == 0)
            result = VenueName.CompareTo(otherMs.VenueName);
        if (result == 0)
            result = otherMs.CreationTime.CompareTo(CreationTime);
        if (result == 0 && Debugger.IsAttached)
            Debugger.Break(); // let the developer know there's a problem
        return result;
    }
    public static int CompareDistanceTo(MealSummary thisMs, MealSummary otherMs) => thisMs.CompareDistanceTo(otherMs);
    #endregion
}
