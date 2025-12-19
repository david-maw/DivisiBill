using DivisiBill.Models;
using System.IO.Compression;
using System.Xml.Serialization;

namespace DivisiBill.Services;

/// <summary>
/// Represents an archive of meals, venues, persons, and related settings for backup or restore operations.
/// </summary>
/// <remarks>The Archive class encapsulates all data necessary to serialize and restore a set of meals and their
/// associated metadata, such as venues and persons. It provides methods for exporting the archive to XML and restoring
/// data from an archive. Use this class to create backups of user data or to restore data from a previous backup. The
/// class supports filtering to include only related or selected items, and handles versioning and date range selection.
/// Thread safety is not guaranteed; synchronize access if used concurrently.</remarks>
public class Archive
{
    #region Shared Declarations
    /// <summary>
    /// Represents a collection of user-specific settings for application preferences and default calculation options.
    /// </summary>
    /// <remarks>This class encapsulates various user preferences, such as default rates for tips and taxes,
    /// display hints, and filtering options. It is used to persist and retrieve user settings to or from an Archive.</remarks>
    public class UserSettingsClass
    {
        public int DefaultTipRate { get; set; }
        public double DefaultTaxRate { get; set; }
        public bool DefaultTipOnTax { get; set; }
        public bool DefaultTaxOnCoupon { get; set; }
        public bool ShowLineItemsHint { get; set; }
        public bool ShowTotalsHint { get; set; }
        public bool ShowVenuesHint { get; set; }
        public bool ShowPeopleHint { get; set; }
        public SimpleLocation FakeLocation { get; set; }
        public string BillsFromDate { get; set; } = null;
        public string BillsToDate { get; set; } = null;
        public bool OnlyRelated { get; set; }
    }
    public static readonly DateTime EarliestDateAllowed = new(2010, 1, 1); 
    #endregion
    #region Constructors
    /// <summary>
    /// Initializes a new instance of the Archive class. Used when an empty class is needed as a basis for a restore operation
    /// from a Meal or list of people or Venues. Also used by the XML serializer.
    /// </summary>
    public Archive() { }

    /// <summary>
    /// Initializes a new instance of the Archive class with the specified meals and archive mode.
    /// </summary>
    /// <param name="mealsToArchive">The list of Meal objects to be included in the archive. Cannot be null.</param>
    /// <param name="onlyRelatedParam">true to archive only related items; otherwise, false to archive all provided meals.</param>
    public Archive(List<Meal> mealsToArchive, bool onlyRelatedParam)
    {
        AllMeals = mealsToArchive;
        SelectedMeals = mealsToArchive;
        PopulateArchive(onlyRelatedParam);
    }
    #endregion
    #region Constructor Helper
    /// <summary>
    /// Initializes the user settings and filters related data collections for the archive based on the specified
    /// parameter.
    /// </summary>
    /// <remarks>When onlyRelatedParam is set to true, the method restricts the Venues, Persons, and
    /// AliasGuids collections to only those referenced by the currently selected meals. Otherwise, all available data
    /// is included. This method also updates the UserSettings property with current application settings and preserves
    /// any previously set date filters.</remarks>
    /// <param name="onlyRelatedParam">true to include only venues, persons, and aliases related to the selected meals; false to include all available
    /// venues, persons, and aliases.</param>
    private void PopulateArchive(bool onlyRelatedParam)
    {
        UserSettings = new UserSettingsClass()
        {
            DefaultTipRate = App.Settings.DefaultTipRate,
            DefaultTaxRate = App.Settings.DefaultTaxRate,
            DefaultTipOnTax = App.Settings.DefaultTipOnTax,
            DefaultTaxOnCoupon = App.Settings.DefaultTaxOnCoupon,
            ShowLineItemsHint = App.Settings.ShowLineItemsHint,
            ShowTotalsHint = App.Settings.ShowTotalsHint,
            ShowVenuesHint = App.Settings.ShowVenuesHint,
            ShowPeopleHint = App.Settings.ShowPeopleHint,
            FakeLocation = App.FakeLocation is not null ? new SimpleLocation(App.FakeLocation) : null,
            OnlyRelated = onlyRelatedParam,
        };
        if (Utilities.IsDebug)
        {
            // this is a handy place to check for differences between the old and new DistributeCosts algorithms
            foreach (Meal m in AllMeals)
                m.CompareCostDistribution();
        }
        if (onlyRelatedParam)
        {
            Venues = [];
            Persons = [];
            AliasGuids = [];
            // figure out what is used by the meals in the list and just include that
            foreach (var meal in SelectedMeals)
            {
                Venue v = Venue.FindVenueByName(meal.VenueName);
                if (v is not null)
                    Venues.Add(v);
                foreach (var pc in meal.Costs.Where(pc => pc.Diner is not null))
                {
                    Persons.Add(pc.Diner);
                    if (pc.PersonGUID != pc.Diner.PersonGUID)
                    {
                        // The item must have used an alias
                        AliasGuids.Add(new GuidMappingEntry() { Key = pc.PersonGUID, Value = pc.Diner.PersonGUID });
                    }
                }
            }
            Venues = [.. Venues.Distinct()];
            Venues.Sort();
            Persons = [.. Persons.Distinct()];
            Persons.Sort();
            AliasGuids = [.. AliasGuids.DistinctBy(a => a.Key)];
            AliasGuids.Sort();
        }
        else
        {
            // No filtering, just include everything
            Venues = [.. Venue.AllVenues];
            Persons = [.. Person.AllPeople];
            AliasGuids = Person.AliasGuidList;
        }
    } 
    #endregion
    #region Data to Archive or Restore
    public string Version { get; set; } = "1.3";
    private DateTimeOffset creationTime = DateTimeOffset.Now;
    public string CreationTimeString
    {
        get => creationTime.ToString();
        set => _ = DateTimeOffset.TryParse(value, out creationTime);
    }
    public string TimeName => Utilities.NameFromDateTime(creationTime.LocalDateTime);
    public UserSettingsClass UserSettings { get; set; } = null;
    public List<Venue> Venues { get; set; } = null;
    public List<Person> Persons { get; set; } = null;
    public List<GuidMappingEntry> AliasGuids { get; set; } = null;

    /// <summary>
    /// Gets or sets the collection of meals currently selected by the user, ordered by CreationTime, oldest first.
    /// This is the list of meals to be archived or restored, depending on the operation the user selects.
    /// </summary>
    [XmlIgnore]
    public List<Meal> SelectedMeals { get; set; } = null;

    /// <summary>
    /// Gets or sets the collection of all the meals associated with this instance, some, or all of them may be selected for restore
    /// (see <see cref="SelectedMeals"/>).
    /// </summary>
    [XmlArray("Meals")]
    public List<Meal> AllMeals { get; set; } = null; 
    #endregion
    #region Serialization
	/// <summary>
    /// Serializes the current object to XML and writes the result to the specified stream.
    /// </summary>
    /// <remarks>The returned stream's position is reset to its original value before serialization. The
    /// caller is responsible for disposing the stream when it is no longer needed.</remarks>
    /// <param name="stream">The stream to which the XML data will be written. If null, a new memory stream is created and used.</param>
    /// <returns>A stream containing the XML representation of the current object, or null if serialization fails.</returns>
    public Stream AsXmlStream(Stream stream = null)
    {
        stream ??= new MemoryStream();
        var originalPosition = stream.Position;
        try
        {
            xmlSerializer.Serialize(stream, this);
            stream.Position = originalPosition;
            return stream;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the XML representation of the current object as a string.
    /// </summary>
    /// <returns>A string containing the XML representation of the object. Returns an empty string if the object cannot be
    /// represented as XML.</returns>
    public string AsXmlString()
    {
        if (AsXmlStream() is Stream stream)
        {
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        return string.Empty;
    }

    public static Archive FromStream(Stream stream)
    {
        try
        {
            return (Archive)xmlSerializer.Deserialize(stream);
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            return null;
        }
    }

    private static readonly XmlSerializer xmlSerializer = new(typeof(Archive));
    #endregion
    #region Restore Archive
    /// <summary>
    /// Filters the available meals (presumably the all those available in an archive) to those created within the specified
    /// date range and updates the selected meals
    /// collection.
    /// </summary>
    /// <remarks>The selected meals are ordered in descending order by creation time. If the collection of
    /// available meals is null, no filtering is performed and 0 is returned.</remarks>
    /// <param name="startDate">The start date of the range. Only meals created on or after this date are included.</param>
    /// <param name="finishDate">The end date of the range. Only meals created on or before this date are included.</param>
    /// <returns>The number of meals selected within the specified date range. Returns 0 if there are no available meals.</returns>
    public int SetDateRange(DateOnly startDate, DateOnly finishDate)
    {
        if (AllMeals is null)
            return 0;
        SelectedMeals = [.. AllMeals.Where(m => DateOnly.FromDateTime(m.CreationTime) >= startDate && DateOnly.FromDateTime(m.CreationTime) <= finishDate).OrderByDescending(m => m.CreationTime)];
        return SelectedMeals.Count;
    }

    /// <summary>
    /// Restores application data from the current in-memory state in an Archive object, optionally deleting existing data and handling
    /// duplicates according to the specified parameters.
    /// </summary>
    /// <remarks>This method disables cloud backup during the restore process to prevent conflicts. If
    /// DeleteBeforeRestore is true, all existing venues, persons, and meals are removed before restoring. When
    /// onlyRelatedParam is true, only data related to the currently selected items is restored, which can be useful for
    /// partial or selective restores. The method also checks for image files associated with restored meals.</remarks>
    /// <param name="DeleteBeforeRestore">true to delete all existing data before restoring data of that class; otherwise, false to merge restored data with existing data.</param>
    /// <param name="OverwriteDuplicates">true to overwrite existing items with the same identifiers during restore; otherwise, false to preserve existing
    /// items and skip duplicates.</param>
    /// <param name="onlyRelatedParam">true to restore only people and venues related to the Meals being restored; otherwise, false to restore all available
    /// data.</param>
    /// <returns>true if the restore operation completes successfully; otherwise, false.</returns>
    public async Task<bool> RestoreAsync(bool DeleteBeforeRestore, bool OverwriteDuplicates, bool onlyRelatedParam)
    {
        // Restore each object type except user specifiable defaults because
        // those are restored through a ViewModel and we want to stay ignorant of those.
        // The rest are list object types, restore individual elements but only if an object of the same name does not exist
        // The presumption is that the lists are in whatever order is deemed correct for their item type
        try
        {
            App.IsCloudAllowed = false; // No backups while this is going on
            if (!onlyRelatedParam)
            {
                // filter other lists to limit them to required items
                List<Venue> FilteredVenues = [];
                List<Person> FilteredPersons = [];
                List<GuidMappingEntry> FilteredAliasGuids = [];
                // figure out what is used by the meals in the list and just include that
                foreach (var meal in SelectedMeals)
                {
                    Venue v = string.IsNullOrWhiteSpace(meal.VenueName) ? null : Venues.FirstOrDefault(venue => meal.VenueName.Equals(venue.Name));
                    if (v is not null)
                        FilteredVenues.Add(v);
                    foreach (var pc in meal.Costs.Where(pc => pc.PersonGUID != Guid.Empty))
                    {
                        Guid personGuid = pc.PersonGUID;
                        GuidMappingEntry guidMappingEntry = AliasGuids.FirstOrDefault(guidMapping => personGuid.Equals(guidMapping.Key));
                        if (guidMappingEntry is not null)
                        {
                            FilteredAliasGuids.Add(guidMappingEntry);
                            personGuid = guidMappingEntry.Value;
                        }
                        Person person = Persons.FirstOrDefault(person => person.PersonGUID == personGuid);
                        if (person is not null)
                            FilteredPersons.Add(person);
                    }
                }
                Venues = [.. FilteredVenues.Distinct()];
                Venues.Sort();
                Persons = [.. FilteredPersons.Distinct()];
                Persons.Sort();
                AliasGuids = [.. FilteredAliasGuids.DistinctBy(a => a.Key)];
                AliasGuids.Sort();
            }
            if (Venues is not null)
            {
                if (DeleteBeforeRestore)
                    Venue.ForgetAllVenues();
                Venue.MergeVenues(Venues, replace: OverwriteDuplicates);
                await Venue.SaveSettingsAsync();
            }
            if (Persons is not null)
            {
                if (DeleteBeforeRestore)
                    Person.AllPeople.Clear();
                Person.AddPeople(Persons, replace: OverwriteDuplicates);
                if (AliasGuids is not null) // only handled if there are persons
                    Person.AliasGuidList = AliasGuids;
                await Person.SaveSettingsAsync();
            }
            if (SelectedMeals is not null)
            {
                if (DeleteBeforeRestore)
                {
                    MealSummary.PermanentlyDeleteAllLocalMeals();

                    // The old current meal is deleted so look for the first meal that is not a fake meal (Size >= 0) to be the new one
                    Meal m = SelectedMeals.Where(m => m.Size >= 0).FirstOrDefault();
                    if (m is null)
                    {
                        // No real meals so just make a fake one current
                        Meal.LoadFake(new MealSummary()).OverwriteCurrent();
                    }
                    else
                    {
                        if (m.OldEnoughToBeNewFile)
                            m.Frozen = true;  // Meaning it has been saved and now you have a new copy which must be saved if changed

                        // Restore the first meal in the list (should be the one that was current at the time of the archive) 
                        m.FinalizeSetup();
                        m.OverwriteCurrent();
                    }
                }
                // The Summary objects will have been created by xmlSerializer so they are brand new and we must figure out whether there are corresponding image files already
                foreach (Meal meal in SelectedMeals)
                    meal.Summary.CheckImageFiles();
                App.HandleActivityChanges(); // So we can check for remote meals if necessary
                await Meal.AddLocalMeals(SelectedMeals, OverwriteDuplicates);
            }
            return true;
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            return false;
        }
        finally
        {
            App.HandleActivityChanges();
        }
    }
    #endregion
    public string Zip(bool saveImages = true)
    {
        if (AllMeals is null || AllMeals.Count == 0)
        {
            Utilities.RecordMsg("No bills to archive");
            return null;
        }
        try
        {
            string xmlFileName = "DivisiBill" + TimeName + ".xml";
            string xmlFilePath = Path.Combine(FileSystem.CacheDirectory, xmlFileName);
            string zipFilePath = Path.ChangeExtension(xmlFilePath, ".zip");
            using (Stream s = new FileStream(xmlFilePath, FileMode.OpenOrCreate))
            {
                s.SetLength(0); // Clear the file if it exists
                AsXmlStream(s);
                s.Flush(); // Ensure the stream is written to disk before zipping
            }
            using (ZipArchive archiveZip = ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
            {
                archiveZip.CreateEntryFromFile(xmlFilePath, xmlFileName);
                File.Delete(xmlFilePath); // Delete the XML file after zipping
                Utilities.DebugMsg($"In {nameof(Zip)}: created zip archive {zipFilePath} containing {xmlFileName}");
                if (saveImages)
                {
                    // Save bill images if requested
                    foreach (var meal in AllMeals.Where(m => m.HasImage && File.Exists(m.ImagePath)))
                    {
                        archiveZip.CreateEntryFromFile(meal.ImagePath, meal.ImageName);
                        Utilities.DebugMsg($"In {nameof(Zip)}: added image {meal.ImageName} to zip archive");
                    }
                }
            }
            return zipFilePath;
        }
        catch (Exception ex)
        {
            ex.ReportCrash("Exception creating Zip Archive");
            return null;
        }
    }
}
