using CommunityToolkit.Mvvm.ComponentModel;
using DivisiBill.Services;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using static DivisiBill.Services.Utilities;
using File = System.IO.File;

namespace DivisiBill.Models;

/// <summary>
/// Persistence logic for Meal: load/save to app storage, files, and cloud, plus age-based lifetime rules.
/// </summary>
public partial class Meal : ObservableObjectPlus
{
    public static void InitializeFolders()
    {
        Directory.CreateDirectory(MealFolderPath);
        Directory.CreateDirectory(SuspectFolderPath);
        Directory.CreateDirectory(DeletedItemFolderPath);
        Directory.CreateDirectory(ImageFolderPath);

        if (Directory.Exists(TempFolderPath))
        {
            // Clean out any old temp files
            foreach (string file in Directory.GetFiles(TempFolderPath))
            {
                try
                {
                    Utilities.DebugMsg($"In InitializeFolders, deleting temp file {file}");
                    File.Delete(file);
                }
                catch
                {
                    // nothing to do
                }
            }
        }
        else
            Directory.CreateDirectory(TempFolderPath);
    }
    #region Persistence Locations
    /// <summary>
    /// Indicated that a current copy is saved to app storage
    /// </summary>
    internal bool SavedToApp { get; set; }
    /// <summary>
    /// Indicates that a current copy is saved to local storage
    /// </summary>
    [XmlIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiagnosticInfo))]
    public partial bool SavedToFile { get; set; }

    /// <summary>
    /// Indicates that a current copy is saved to remote storage
    /// </summary>
    [XmlIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiagnosticInfo))]
    public partial bool SavedToRemote { get; set; }
    #endregion
    #region Load
    /// <summary>
    /// Read a meal from App local storage, by the time this is called LocalMealList has been populated, so if the Meal is in it use the 
    /// MealSummary from the list rather than creating a new one.
    /// </summary>
    /// <returns>The stored Meal or null if there wasn't one</returns>
    public static Meal LoadFromApp(bool tryExistingSummary)
    {
        string myString = App.Settings.StoredMeal;
        if (string.IsNullOrWhiteSpace(myString))
            DebugMsg("In Meal.LoadFromApp, no stored meal found");
        else
        {
            byte[] buf = Encoding.UTF8.GetBytes(myString);
            MemoryStream s = new(buf);
            Meal m = LoadFromStream(s);
            DebugMsg("in Meal.LoadFromApp meal = " + m.Summary);
            MealSummary existingMealSummary = tryExistingSummary ? LocalMealList.Where(ms => ms.Id.Equals(m.Summary.Id)).FirstOrDefault() : null;
            m.SavedToApp = true;
            m.SavedToFile = existingMealSummary is not null;
            if (m.SavedToFile)
            {
                // The normal case where the meal is in local storage
                m.Summary = existingMealSummary;
            }
            else
            {
                // An unusual case where the meal probably came from remote storage though it's possibly a test meal which is not stored at all
                m.SavedToRemote = App.Settings.MealSavedToRemote;
                m.Summary.IsRemote = m.SavedToRemote;
            }
            m.Frozen = App.Settings.MealFrozen;
            m.MonitorChanges = true; // From now on take notice of changes
            return m;
        }
        return null;
    }
    /// <summary>
    /// Deserializes a Meal object from the specified stream, optionally applying a provided MealSummary and performing
    /// setup operations.
    /// </summary>
    /// <remarks>The method resets certain state flags and updates cost calculations on the returned Meal. If
    /// the stream is empty or an error occurs during deserialization, the returned Meal will have its CreationReason
    /// property set to indicate the issue. The caller is responsible for ensuring the stream is positioned at the
    /// beginning and remains open for the duration of the operation.</remarks>
    /// <param name="sourceStream">The stream containing the serialized Meal data. The stream must be readable and positioned at the beginning. If
    /// the stream is null or empty, a placeholder Meal is returned.</param>
    /// <param name="ms">An optional MealSummary to associate with the deserialized Meal. If provided, this summary replaces any summary
    /// present in the serialized data.</param>
    /// <param name="setup">true to perform additional setup operations on the deserialized Meal, such as initializing event handlers;
    /// otherwise, false.</param>
    /// <returns>A Meal object deserialized from the stream. If deserialization fails or the stream is empty, a placeholder Meal
    /// is returned with details indicating the failure.</returns>
    public static Meal LoadFromStream(Stream sourceStream, MealSummary ms = null, bool setup = true)
    {

        if (sourceStream is null || sourceStream.Length == 0)
        {
            // There's nothing in the stream, so no point trying to deserialize it, return a fake MealSummary
            return new Meal() { VenueName = "Bad Bill", Size = -2, CreationReason = "Empty file" };
        }
        Meal m;
        try
        {
            Trace.Assert(sourceStream.Position == 0, "Source stream expected to be positioned at 0");
            DebugExamineStream(sourceStream);
            LineItem.NextItemNumber = 1;
            m = (Meal)MealSerializer.Deserialize(sourceStream);
            if (ms is not null)
                m.Summary = ms; // Discard the one that was created as part of the deserialize operation in favor of the passed one 
            if (m.Summary.SnapshotStream is null)
                m.Summary.SnapshotStream = new MemoryStream(3000);
            if (sourceStream != m.Summary.SnapshotStream)
            {
                sourceStream.Position = 0;
                m.Summary.SnapshotStream.SetLength(0); // discard any previous contents
                sourceStream.CopyTo(m.Summary.SnapshotStream);
            }
            // Flag as saved so it's not accidentally saved as a side effect of the following calls
            m.SavedToApp = true;
            m.SavedToFile = true;
            m.SavedToRemote = true;
            m.UpdateAmounts();
            // Assign each known guid, not strictly necessary here but it's a handy spot
            foreach (PersonCost personCost in m.Costs.Where(pc => !pc.PersonGUID.Equals(Guid.Empty)))
            {
                personCost.SetDinerFromGuid();
            }
            m.DistributeCosts(); // Make sure the calculations are up to date
            if (setup)
                m.SetupChangedEvents();
            // Probably one of these will be set true by the caller, but we don't know which, so just reset them all
            m.SavedToApp = false;
            m.SavedToFile = false;
            m.SavedToRemote = false;
            m.Size = sourceStream.Length;
            if (m.OldEnoughToBeNewFile)
                m.Frozen = true;  // Meaning it has been saved and now you have a new copy which must be saved if changed
        }
        catch (Exception ex)
        {
            ReportCrash("MethodName", "LoadFromStream", sourceStream, ex, "suspect.xml");
            m = new Meal
            {
                CreationReason = ex.Message,
                Size = -1, // flag that we have no clue
                VenueName = "Bad bill"
            };
        }
        return m;
    }
    internal static Meal LoadFromSavedStream(MealSummary ms, bool setup = false)
    {
        ms.SnapshotStream.Position = 0;
        Meal m = LoadFromStream(ms.SnapshotStream, ms, setup);
        if (m is null)
        {
            // The stream was bad so just return null
            DebugMsg($"In Meal.LoadFromFile: LoadFromStream returned null for {ms.FileName}");
            if (Utilities.IsDebug)
                Debugger.Break();
        }
        else
        {
            m.MonitorChanges = true;
        }
        return m;
    }
    public static Meal LoadFromFile(MealSummary ms, bool setup = false)
    {
        Meal m = null;
        string TargetFileName = ms.FileName;
        try
        {
            using FileStream sourceStream = File.OpenRead(Path.Combine(MealFolderPath, TargetFileName));
            m = LoadFromStream(sourceStream, ms, setup);
            if (m is null)
            {
                // The stream was bad so just return null
                Utilities.DebugMsg($"In Meal.LoadFromFile: LoadFromStream returned null for {ms.FileName}");
                if (Utilities.IsDebug)
                    Debugger.Break();
            }
            else
            {
                if (m.CreationTime == DateTime.MinValue || m.Size < 0) // It's a file without a stored creation time
                    m.Summary.SetCreationTimeFromFileName(TargetFileName);
                m.Summary.IsLocal = true;
                m.SavedToFile = true;
                if (m.Size < 0)
                {
                    MoveSuspectFile(TargetFileName);
                    m.Summary.VenueName = "Suspect File - will hide";
                    if (Utilities.IsDebug)
                        Debugger.Break();
                }
                if (Utilities.IsDebug && App.InitializationComplete.Task.IsCompleted) // don't do this until we're well into initialization
                {
                    // this is a handy place to check for differences between the old and new DistributeCosts algorithms
                    m.CompareCostDistribution();
                }
                m.MonitorChanges = true;
            }
        }
        catch (FileNotFoundException)
        {
            // Could theoretically happen if someone is messing with the file system, so if it does, just return null
            return null;
        }
        return m;
    }
    // Move a suspect file into a different folder so it doesn't keep causing trouble
    public static void MoveSuspectFile(string TargetFileName) => File.Move(Path.Combine(MealFolderPath, TargetFileName), Path.Combine(SuspectFolderPath, TargetFileName));
    public static async Task<Meal> LoadFromRemoteAsync(MealSummary ms, bool setup = false)
    {
        Meal m = null;
        using (Stream sourceStream = await RemoteWs.GetItemStreamAsync(RemoteWs.MealTypeName, ms.Id))
        {
            m = LoadFromStream(sourceStream, ms, setup);
            if (m is null || m.Size <= 0)
            {
                // The stream was bad so just return null
                Utilities.DebugMsg($"In Meal.LoadFromRemoteAsync: LoadFromStream returned null for {ms.Id}");
                m = null;
            }
            else
            {
                if (App.IsCloudImageBackupAllowed && ms.HasRemoteImage && !ms.HasImage) // Meaning there is a remote image but not a local one
                {
                    // Load the remote image as well
                    await LoadImageFromRemoteAsync(ms);
                }
                m.Summary.IsRemote = true;
                m.SavedToRemote = true;
                m.MonitorChanges = true;
            }
        }
        return m;
    }
    public static async Task<Meal> LoadAsync(MealSummary ms, bool setup = false)
    {
        if (App.IsCloudImageBackupAllowed && ms.IsRemote && !ms.IsLocal && ms.HasRemoteImage && !ms.HasImage) // it is a remote meal with a remote image we don't have
        {
            // Load the remote image as well
            await LoadImageFromRemoteAsync(ms);
        }
        Meal m = ms.SnapshotValid
            ? LoadFromSavedStream(ms, setup: setup)
            : ms.IsLocal ? LoadFromFile(ms, setup: setup) : ms.IsRemote ? await LoadFromRemoteAsync(ms, setup) : LoadFake(ms);
        return m;
    }
    /// <summary>
    /// Attempts to download the remote image associated with the specified meal summary asynchronously.
    /// </summary>
    /// <remarks>If the meal summary does not have a remote image or is null, the method returns <see
    /// langword="false"/> without performing any operation.</remarks>
    /// <param name="ms">The meal summary for which to download the remote image. Must not be null and must indicate that a remote image
    /// is available.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the image was
    /// successfully downloaded; otherwise, <see langword="false"/>.</returns>
    public static async Task<bool> LoadImageFromRemoteAsync(MealSummary ms)
    {
        if (ms is null || !ms.HasRemoteImage) // nothing to do
            return false;
        if (await RemoteWs.DownloadImageFileAsync(ms.ImagePath, ms.IsEncrypted))
        {
            ms.CheckImageFiles();
            return true;
        }
        return false;
    }
    #endregion
    #region Save
    private TimeSpan IdleTime => DateTime.Now - LastChangeTime;
    public bool TooOldToContinue => IdleTime > App.MaximumIdleTime;
    public bool OldEnoughToBeNewFile => IdleTime > App.MinimumIdleTime;
    internal void SaveToApp()
    {
        Utilities.DebugMsg($"In Meal.SaveToApp");
        byte[] buf = new byte[10000];
        MemoryStream s = new(buf);
        SaveToStream(s);
        string myString = Encoding.UTF8.GetString(buf, 0, (int)s.Position);
        if (Utilities.IsWinUI && myString.Length > 4096) // too large to store on Windows
        {
            Utilities.DisplayAlertAsync("Error", $"Bill is too large ({myString.Length * 2} bytes) to store in App on Windows");
            myString = string.Empty;
        }
        App.Settings.StoredMeal = myString;
        App.Settings.MealFrozen = Frozen;
        App.Settings.MealSavedToRemote = SavedToRemote;
        App.Settings.MealSavedToFile = SavedToFile;
        SavedToApp = true;
        // No need to save the bill image if there is one, it is already in the internal store 
    }
    public void SaveToStream(Stream streamParameter)
    {
        SaverVersion = Utilities.VersionName;
        DataVersion = "1.1"; // Increment when significant changes happen to the data format, like optional fields being added
        using (StreamWriter sw = new(streamParameter, Encoding.UTF8, -1, true))
        using (var xmlWriter = XmlWriter.Create(sw, new XmlWriterSettings() { Indent = true, OmitXmlDeclaration = true, NewLineOnAttributes = true }))
        {
            XmlSerializerNamespaces namespaces = new();
            namespaces.Add(string.Empty, string.Empty);
            MealSerializer.Serialize(xmlWriter, this, namespaces);
        }
        DebugExamineStream(streamParameter);
    }
    public void SaveToSnapshot()
    {
        Stream s = Summary.SnapshotStream;
        s ??= Summary.SnapshotStream = new MemoryStream(3000);
        // Clear out the snapshot
        s.Position = 0;
        s.SetLength(0);
        // repopulate it with the persisted Meal
        SaveToStream(s);
    }
    /// <summary>
    /// Save this meal to a local file, alas there's no async file IO in .NET Standard 2.0 which
    /// is what Xamarin Forms works best with. Consequently we are actually just running synchronous code
    /// on a worker thread.
    /// 
    /// The file access will sometimes fail with a sharing violation if another thread happens to be accessing the same 
    /// file. If that happens we just wait a bit and try again. 
    /// </summary>
    /// <returns></returns>
    public async Task<bool> SaveToFileAsync()
    {
        if (IsDefault) // never save the default bill
            return false;
        while (!SavedToFile) // Another save beat us to it, no point saving the file again, just exit
        {
            try
            {
                await Task.Run(SaveToFile);
                return true;
            }
            catch (IOException ex) when (ex.Message.StartsWith("Sharing violation"))
            {
                // nothing to do
            }
            await Task.Delay(500); // There was probably another save going on, give it time to finish
        }
        return false;
    }
    internal void SaveToFile()
    {
        Debug.Assert(!(this == CurrentMeal && !IsLastChangeTimeSet && SavedToFile), "Should not store an unchanged current meal");
        if (Costs.Count == 0 && LineItems.Count == 0) // This is an empty bill, do not store it
        {
            DebugMsg($"In Meal.SaveToFile: Bill {Summary.Id} is empty, ignoring it");
            SavedToFile = true; //don't bother trying again until it is changed
            return;
        }
        string TargetFilePath = FilePath;
        using FileStream stream = File.Open(TargetFilePath, FileMode.Create); // Overwrites any existing file
        SaveToSnapshot();
        Summary.SnapshotStream.Position = 0;
        Summary.CopySnapshotTo(stream);
        // Set some file attributes so they'll match the persisted data in the file
        File.SetCreationTime(TargetFilePath, Summary.CreationTime);
        File.SetLastWriteTime(TargetFilePath, Summary.LastChangeTime);
        Size = stream.Length;
        Summary.IsLocal = true;
        SavedToFile = true;
        if (SavedToApp)
            MainThread.InvokeOnMainThreadAsync(() => App.Settings.MealSavedToFile = true);
    }
    internal async Task SaveToRemoteAsync()
    {
        if (Costs.Count == 0 && LineItems.Count == 0) // This is an empty bill, do not store it
        {
            DebugMsg($"In Meal.SaveToRemoteAsync: Bill {Summary.Id} is empty, ignoring it");
            SavedToRemote = true; //don't bother trying again until it is changed
            return;
        }
        if (Summary.SnapshotValid)
        {
            bool wasEncrypted = Summary.IsEncrypted;
            Summary.SnapshotStream.Position = 0;
            Size = Summary.SnapshotStream.Length;
            SavedToRemote = Summary.IsRemote = await RemoteWs.PutMealStreamAsync(Summary, Summary.SnapshotStream);
            if (this == CurrentMeal)
                App.Settings.MealSavedToRemote = true;
            // Decide if we need to back up the image as well
            if (SavedToRemote && Summary.HasImage && // Our Meal is remote and we have a local image
                (!(slowImageBackupQueue.Contains(Summary) || imageBackupQueue.Contains(Summary))) && // not already queued
                (!Summary.HasRemoteImage // the image is not currently stored remotely
                || (Summary.IsEncrypted != wasEncrypted))) // or the remote image has a different encryption state to the meal (because it has changed)
            {
                QueueForImageBackup(Summary);
            }
        }
    }
    /// <summary>
    /// Called whenever a user tells us it's time to persist a file. This is a special action - it persists a snapshot of the
    /// current bill right now, but tries not to otherwise disturb the bill. We clone the 
    /// current Meal and work exclusively on the clone sidestepping any issues of where the current bill may be stored.
    /// If there claims to be an image but actually isn't, we just ignore it so as to be as resilient as possible
    /// </summary>
    public async Task SaveSnapshotAsync()
    {
        // First clone the Meal
        Meal m = LoadFromApp(tryExistingSummary: false); // load up the meal with an independent summary
        // From now on we deal only with the cloned Meal
        m.SaveReason = "Command"; // Does not need to be preserved since all saves change it
        // Now make the creation time be now so the file is saved with a distinct name
        if (!m.IsLastChangeTimeSet)
            m.ActualLastChangeTime = m.CreationTime;
        m.Summary.CreationTime = DateTime.Now; // Do NOT set Meal.CreationTime here it will cause a save as a side effect
        m.Frozen = false;
        m.SavedToApp = false;
        m.SavedToFile = false;
        m.SavedToRemote = false;
        m.Summary.IsLocal = true; // because the file will be stored locally below
        m.Summary.IsRemote = false;
        m.Summary.HasRemoteImage = false; // because the image of this new bill is not stored remotely (yet)
        if (HasImage && File.Exists(ImagePath)) // Copy the image file to the new location if it exists
        {
            File.Copy(ImagePath, m.ImagePath, true);
            m.Summary.CheckImageFiles(); // Make sure the summary reflects the image
        }
        await m.SaveToFileAsync();
        // Now make the snapshot visible
        m.Summary.Show();
    }
    /// <summary>
    /// Called whenever we think this Meal might have changed so we can persist the old version to permanent 
    /// storage in the list of files and the application store. Also inspects an idle bill to see if
    /// maybe it is old enough to trigger making it permanent and starting a new one. 
    /// </summary>
    public async Task SaveIfChangedAsync(bool SaveFile = true, bool SaveRemote = true)
    {
        if (!Frozen) // Frozen meals have already been persisted
        {
            if (!SavedToApp)
                SaveToApp();
            if (SaveFile && !SavedToFile)
                await SaveToFileAsync();
            if (SaveRemote && App.IsCloudAllowed && !SavedToRemote)
                await SaveToRemoteAsync();
            if ((DateTime.Now - LastChangeTime) > TimeSpan.FromMinutes(10)) // The bill has not been changed for a while
                await TrySaveOldBillAsync();
        }
    }
    /// <summary>
    /// Inspect the current bill and if it is old enough to be deemed a new bill
    /// rather than the continuation of an old one, then see if it is appropriate
    /// to save off the old version (meaning save it if it hasn't already been saved).
    /// </summary>
    public static async Task<bool> TrySaveOldBillAsync([CallerMemberName] string methodName = null, [CallerLineNumber] int callerLineNumber = 0)
    {
        Utilities.DebugMsg($"In TrySaveOldBillAsync, called from {methodName} at {callerLineNumber}");
        if (CurrentMeal is not null && CurrentMeal.TooOldToContinue && !CurrentMeal.Frozen) // The bill is old, start a new one
        {
            Utilities.DebugMsg("In TrySaveOldBillAsync, marking copy of existing meal as new");
            await CurrentMeal.MarkAsNewAsync("ElapsedTime");
            return true;
        }
        return false;
    }
    #endregion
    #region Archive
    /// <summary>
    /// Creates a ZIP archive containing the current object's data as an XML file, and optionally includes an associated
    /// image if available.
    /// </summary>
    /// <remarks>The ZIP archive is saved in the application's temp directory (see <see cref="Archive.ZipAsync"/>) and includes an XML file
    /// representing the object's data. If an image is associated with the object and exists on disk, it is also
    /// included in the archive. The method handles any exceptions internally and reports them, returning an empty
    /// string if an error occurs.</remarks>
    /// <returns>A string containing the full file path to the created ZIP archive. Returns an empty string if the archive could
    /// not be created due to an error.</returns>
    public string CreateZipArchive()
    {
        try
        {
            Archive archive = new([this], true);
            // Create the XML file in the cache directory
            string zipFileFullname = archive.ZipAsync();
            // At this point we have a zip archive file on disk containing a single XML file containing the archive data and possibly an image file too
            return zipFileFullname;
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            Utilities.DebugMsg($"In {nameof(CreateZipArchive)}: exception creating zip archive: {ex.Message}");
            return string.Empty;
        }
    }
    #endregion
}