using CommunityToolkit.Mvvm.ComponentModel;
using DivisiBill.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using static DivisiBill.Services.Utilities;
using Directory = System.IO.Directory;
using File = System.IO.File;

namespace DivisiBill.Models;

/*
 * Meals and their lifetimes.
 * 
 * Note that the terms meal and bill are synonymous, for historical reasons the objects are called meals
 * internally but public documentation calls them bills.
 * 
 * The objective of the bill lifetime management algorithm is to store bills at a logical time rather than
 * have an explicit 'save' operation. The idea being that the user starts with some bill (either a default 
 * one, or an old one, makes changes to it, looks at the result, then shuts down the app until next time they
 * have a bill to process. At the same time we want to lose as little data as possible if the application halts
 * abruptly (like during an application failure or a system reboot).
 * 
 * The main use cases are:
 * 1) A user enters a bill, does nothing for a while, then reuses the same bill for a different event.
 *    This should trigger saving the old version of the bill.
 * 
 * 2) A user enters a bill, pauses for a while, then replaces it with a stored bill.
 *    This should trigger saving the first bill. 
 *    
 * 3) A user loads a stored bill, then replaces it with a stored bill.
 *    This should not trigger a new bill to be stored, it should do nothing except a periodic save for 
 *    safety (see below) but does lead to case 1 or 2.
 * 
 * 4) A user loads a stored bill, then edits it
 *    This should do nothing except a periodic save for safety (see below) but does lead to case 1 or 2.
 *    
 * 5) A user loads a stored bill, updates it to remove items then pauses (perhaps to actually consume a meal).
 *    After the pause of many minutes they scan a bill image and apply it to the prepared bill. This should 
 *    do nothing except a periodic save for safety (see below) but does lead to case 1 or 2.
 * 
 * The consequence of all this is that a bill is not added to the list of stored bills until we are sure it
 * is finished with, and that only happens when more than 90 minutes (App.MinimumIdleTime) have elapsed
 * and something important (like the venue name) changes. After an additional hour (App.MaximumIdleTime) has 
 * elapsed since the bill was changed any subsequent change represents a new bill. The current bill is evaluated
 * only occasionally (currently when the app initializes, when a bill is loaded, when items from a scanned bill 
 * are inserted or when a venue is changed - look for uses of MarkAsNewAsync to see this).
 * 
 * More specifically the rule is that you can change a bill as often as you like for the first 
 * portion of its life and we'll always assume it's just the same bill being edited and won't store it.
 * After that, venue changes or loading another bill will trigger storing the current bill (if it has been changed). 
 * After the bill is MaximumIdleTime old a scan at program restart will cause it to be persisted and marked as "frozen", 
 * after that it can still be viewed but any attempt to change it simply results in it being viewable in the list of bills
 * and a new bill for the same venue is created. Even if the program is not restarted, if a bill goes 10 
 * minutes without an update it will be checked to see if it has aged out and if necessary frozen so a new one will be started
 * by the next update. 
 * 
 * In order to protect against data loss in an unexpected restart changed bills are periodically backed up to 
 * the application dictionary by Meal.PeriodicSaveAsync which just loops checking for updates periodically or to be
 * asked to do an immediate backup by RequestSnapshot because something important (like a Venue name) changed. 
 * 
 * In order to prevent data loss in the event of a more significant event (like replacing a phone or uninstalling the
 * application) snapshots to local and cloud backups are made. The advent of Android SDK 30 with it's prohibition
 * on the use of shared files makes the file backup less than helpful for replacing the app but the cloud backup works.
 * 
 * The implementation is that there are a list of meals (bills) stored locally in XML files and, optionally, 
 * images (in JPG files) a list of those files is in LocalMealList which has pointers to a MealSummary
 * for each meal. Each MealSummary includes the name of the file it is in and the name of the image if there is one. All the 
 * meals are in that list with the exception of the current meal.
 * 
 * The current meal is stored in the application dictionary - it may also be stored on disk, but might not be.
 * For example it is persisted to disk when the program exits and periodically if it changes. The same
 * the same bill will be reloaded when the program next starts although it may be marked as Frozen depending how old it is.
 * 
 * Any change to a Frozen bill results in it being persisted to local storage (aka disk) and a new copy made (with
 * a new creation time) for subsequent updates.
 * 
 * The files are stored in public external storage for debug builds, so from outside you see a DivisiBillDebug
 * folder in Phone/Documents and within it are Meals and Images folders containing Meals and their images
 * respectively. Internally Android exposes this as /Storage/Emulated/0/Documents/DivisiBillDebug. The release
 * build files are in an app-private folder (/data/user/0/com.autoplus.divisibill2/files)
 * 
 * So there's always exactly one current meal, the question is when to persist it to a new file, in 
 * other words when is it a distinct bill, and when is it an existing one you've updated some more (case 4 above).
 * Initially a meal is marked as SavedToApp and SavedToFile = true and whenever anything significant is done 
 * to it, it is marked as SavedToApp and SavedToFile = false. When certain actions are performed we check whether
 * it has been marked changed (SavedToApp or SavedToFile are false), and if it has we persist the file in XML to
 * either the app dictionary or a file, or both (done by calling SaveIfChangedAsync) this is also one of 
 * the opportunities to see if it is appropriate to save to disk the version of the meal preceding the change
 * by calling MarkAsNewAsync and passing a parameter to say why it seemed worthwhile to save a snapshot of the meal.
 * 
 * Once a meal is saved the current meal is marked as Frozen and unchanged and the name of the file it is 
 * stored in is saved (mostly for historical reasons, this algorithm used to be different). If a frozen meal
 * is marked as changed the meal CreationTime is set as well as resetting Frozen and clearing the storage file name. 
 * 
 * The very first time the program runs there won't be a stored bill, so we create one and mark it as SavedToApp and
 * SavedToFile = true and IsDefault - such a bill never needs storing to a permanent file until it is changed (just 
 * setting Frozen would do, but IsDefault allows for a check at storage time).
 * 
 * Several attributes control all this, the app has MealLoadName - the name of the file containing the most recently loaded bill and
 * MealLoadTime - the time when it was updated (so it's a bit of a misnomer). These are hangovers from the old algorithm.
 * 
 * Each meal itself has a variety of interesting properties:
 *    Summary - a reference to the mealSummary for this Meal
 *    SavedToFile - means it has been persisted to local storage (meaning it is in a file named according to the
 *       CreationTime of the bill) since the last time it was updated.
 *    Summary.IsLocal - meaning this MealSummary represents a Meal stored in a local file, possibly not the
 *       latest version (SavedToFile is what indicates that)
 *    CreationTime - when it was created (the time when a frozen meal was first changed)
 *    LastChangeTime - when it was most recently changed (the time when a frozen bill was last changed)
 *    Frozen - means we've persisted a copy, so CreationTime needs to be updated next time the bill is changed
 *    IsDefault - is it an unmodified sample meal the program created - this never needs to be saved
 *    SavedToApp - means it was not changed since last being persisted to the app dictionary, 
 *    TooOldToContinue - This basically says an existing bill that is about to be updated is really a new bill for 
 *        the same venue, in essence you can keep updating a bill for Meal.Timelimit (3 hours) but after that we'll 
 *        store a snapshot of it and start a new bill. This mostly happens when initially loading a bill on startup.
 *    OldEnoughToBeNewFile - it was last updated more than App.MinimumIdleTime (15 minutes) ago. This is basically how we
 *        decide if an existing bill that is being saved to disk should be saved to the same file or to a new one.
 *    
 *    The key methods are:
 *       MarkAsNewAsync(string why) - flags a meal as having been recreated (it is created with changed false), this may 
 *          cause the old version to be saved to a file if it has unsaved changes (SavedToFile is false). There are
 *          several reasons to do this:
 *             1) The current bill is so old it must be saved before making updates, this only happens
 *                just after starting the program. 
 *             2) The Venue Name has been changed, so it's obviously now a bill for a new location
 *             3) The current bill is being replaced by an old one
 *             4) It has been a while since the last update
 *          If a bill is old enough for changes to be stored as a new bill (default 15 minutes) when MarkAsNewAsync is
 *          called it is marked as Frozen, so any subsequent change will start a new bill.
 *       SaveIfChangedAsync(string why) - if the meal has changed, then save it (to the app dictionary, local and/or 
 *           remote storage)
 *       MakeVisible - if a meal has been saved to disk add it to the LocalMealList so it is visible
 *       
 *    So the general idea is that we flag a bill as changed whenever something which would be persisted changes in the bill
 *    and call SaveIfChanged periodically so if the app, or system crashes, you'll be able to recover from a recent point. 
 *    We periodically save the current bill to a file in case DivisiBill is uninstalled, when the current bill in the app 
 *    dictionary would be lost. Less frequently, we save the current bill to the cloud, just in case a real catastrophe 
 *    happens and all local bills are lost (as of Android 30 this can happen if you uninstall the app).
 *    
 *    It is important not to mark bills as changed when values which are not persisted change so that, for example, calculating the
 *    subtotal on a newly loaded bill has no effect.
 *    
 *    Meal images are handled as distinct files, the most recent image, if there is one, is always in a file
 *    named like the Meal file, but with a JPG extension instead of XML. As of 2022 image processing is used to shrink 
 *    images but they are still 10s of kB so they are much larger than Meal files which are typically 2kB.
*/

[DebuggerDisplay("{DebugDisplay}")]
public partial class Meal : ObservableObjectPlus
{
    #region Global
    private ObservableCollection<PersonCost> costs = new ObservableCollection<PersonCost>();
    private ObservableCollection<LineItem> lineItems = new ObservableCollection<LineItem>();
    private double taxRate;
    private double tipRate;
    private bool tipOnTax, isCouponAfterTax;
    private MealSummary summary;

    // Static items shared by all instances of the class
    public const string MealFolderName = "Meals";
    public const string SuspectFolderName = "Suspect";
    public const string DeletedItemFolderName = "Deleted";
    public const string ImageFolderName = "Images";
    private static XmlSerializer mealSerializer = null;
    private static XmlSerializer MealSerializer => mealSerializer ??= new XmlSerializer(typeof(Meal));
    #endregion
    #region Construction
    public Meal() // public constructor needed for deserialization
    {
        // Set up required objects
        savedLineItems = new List<LineItem>();
        MonitorChanges = false;
    }

    private static bool classIsInitialized = false;
    public static async Task InitializeAsync()
    {

        if (!classIsInitialized)
        {
            try
            {
                classIsInitialized = true;
                await StatusMsgAsync("Starting Meal.InitializeAsync");
                if (App.Settings is null) // for testing
                    App.Settings = new AppSettings();
                await GetLocalMealListAsync(); ; 
                if (LocalMealList.Count == 0 && Utilities.IsDebug)
                {
                    await StatusMsgAsync("Creating fake bill list so we have something to work with");
                    CreateFakeStoredBills();
                }
                Meal AppMeal = LoadFromApp();

                if (!App.RecentlyUsed && AppMeal is not null && AppMeal.TooOldToContinue)
                {
                    // Determine which mealSummary is closest so we can use it instead of the old one
                    MealSummary closestMealSummary = null;
                    Venue closestVenue = null;
                    foreach (var ms in LocalMealList)
                    {
                        if (App.UseLocation)
                        {
                            Venue v = Venue.FindVenueByName(ms.VenueName);
                            ms.Distance = v is null ? Distances.Unknown : v.Distance;
                            if (v is not null && (closestVenue is null || closestVenue.CompareDistanceTo(v) > 0))
                            {
                                closestMealSummary = ms;
                                closestVenue = v;
                            }
                        }
                    }
                    if (closestMealSummary is not null && AppMeal.Summary.CompareDistanceTo(closestMealSummary) > 0)
                        CurrentMeal = LoadFromFile(closestMealSummary, true);
                }
                CurrentMeal ??= AppMeal;
                if (CurrentMeal is null)
                {
                    CurrentMeal = new Meal();
                    CurrentMeal.LoadFakeSettings();
                    CurrentMeal.Summary.CreationTime = DateTime.MinValue; // flag the meal as default with no side effects
                }
                Application.Current.Resources["MealViewModel"] = new ViewModels.MealViewModel(); // Reinitialize MealViewModel
                SnapshotNeeded.IsPaused = true;
                App.StartBackupLoop();
                bool saved = await TrySaveOldBillAsync();
                Utilities.DebugMsg("Completed TrySaveOldBillAsync, returned" + (saved ? " saved" : " not saved"));
            }
            catch (Exception ex)
            {
                ex.ReportCrash();
                await StatusMsgAsync("Meal.InitializeAsync faulted: " + ex.Message);
                await Task.Delay(10000); // enough time to read the message
            }
        }
    }
    /// <summary>
    /// Used when restarting without reinitializing
    /// </summary>
    /// <returns></returns>
    public static async Task RestartAsync()
    {
        await TrySaveOldBillAsync();
        Application.Current.Resources["MealViewModel"] = new ViewModels.MealViewModel(); // The Resource dictionary was rebuilt, so put this back
    }
    /// <summary>
    /// Used when App is resuming
    /// </summary>
    /// <returns></returns>
    public static async Task ResumeAsync()
    {
        await TrySaveOldBillAsync();
        if (!App.RecentlyUsed && CurrentMeal is not null && CurrentMeal.TooOldToContinue) // Maybe we need to replace the current meal with the closest one
        {
            MealSummary ClosestMealSummary = CurrentMeal.Summary;
            foreach(var ms in LocalMealList.Where(ms1=>ms1.CompareDistanceTo(ClosestMealSummary) < 0))
                ClosestMealSummary = ms;
            if (ClosestMealSummary != CurrentMeal.Summary)
            {
                Meal closestMeal = LoadFromFile(ClosestMealSummary, true);
                if (closestMeal!=null) 
                    closestMeal.OverwriteCurrent();
            }
        }
    }

    private static readonly PauseTokenSource SnapshotNeeded = new PauseTokenSource();

    public static void RequestSnapshot()
    {
        Utilities.DebugMsg($"In RequestSnapshot");
        SnapshotNeeded.IsPaused = false;
    }
    public void Clear()
    {
        // Clear everything out but leave the bill image (if any) alone
        ScannedSubTotal = 0;
        ScannedTax = 0;
        LineItems.Clear();
        Costs.Clear();
        RoundedAmount = 0;
        // Now ensure it is not saved, preserving any persisted version 
        SavedToFile = true;
        SavedToRemote = true;
        Summary.IsLocal = false;
        Summary.IsRemote = false;
        Frozen = true;
        SaveToApp(); // We do want to save it to App storage so the old version doesn't reappear after a restart
    }
    /// <summary>
    /// Call this on a meal to have it overwrite the current one and (via events) trigger actions like regenerating meal lists 
    /// </summary>
    private void OverwriteCurrent()
    {
        Meal PriorMeal = CurrentMeal;
        CurrentMeal = this;
        // It is important to reassign CurrentMeal early so downstream code which wants to remove it from lists of meals
        // will recognize the correct meal. Such code may well be triggered by events, so beware.

        Application.Current.Resources["MealViewModel"] = new ViewModels.MealViewModel(); // Reinitialize MealViewModel;
        CurrentMeal.SaveToApp();
    }
    /// <summary>
    /// Save the current meal if necessary then show it in the list of meals and hide the new selection from that list.
    /// Take "this" and point Meal.CurrentMeal at it, then update various references so they'll also point at the new CurrentMeal
    /// and save a copy to the app local storage so it'll be recovered if the app restarts. 
    /// </summary>
    /// <returns></returns>
    public async Task BecomeCurrentMealAsync()
    {
        await Saver.SaveCurrentMealIfChangedAsync("Reloaded");
        OverwriteCurrent();
    }
    private void PersonCostRenumber(PersonCost pcToChange, LineItem.DinerID newUnusedDinerID)
    {
        // Validity check - ensure the new ID is unused
        if (null != Costs.FirstOrDefault(pc => pc.DinerID == newUnusedDinerID))
            return;
        LineItem.DinerID oldDinerID = pcToChange.DinerID;
        pcToChange.DinerID = newUnusedDinerID; // Important to do this first
        foreach (var li in LineItems.Where(li => li.GetShares(oldDinerID) > 0))
            li.TransferShares(newSharerID: newUnusedDinerID, oldSharerID: oldDinerID);
    }
    public void CostListResequence()
    {
        LineItem.DinerID desiredID = LineItem.DinerID.first;
        try
        {
            foreach (var pc in Costs.ToList())
            {
                if (pc.DinerID != desiredID)
                    PersonCostRenumber(pc, desiredID);
                desiredID++;
            }
        }
        catch (Exception ex)
        {
            Utilities.DebugMsg("In Meal.CostListResequence, exception: " + ex);
        }
    }
    public static Meal CurrentMeal { get; private set; }

    /// <summary>
    /// Loop saving the bill locally as necessary
    /// </summary>
    /// <param name="delayTime"></param>
    /// <returns></returns>
    public static async Task PeriodicSaveAsync(int delayTime)
    {
        Utilities.DebugMsg($"Enter Meal.PeriodicSaveAsync({delayTime} seconds) awaiting InitializationComplete");
        await App.InitializationComplete.Task;
        Utilities.DebugMsg($"In Meal.PeriodicSaveAsync InitializationComplete happened");
        for (int i = 0; i < 1000; i++) // test - wait until we explicitly allow continue 
        {
            if (!App.pauseInitialization) break;
            await Task.Delay(10000);
        }
        while (true)
        {
            if (!(bool)CurrentMeal?.SavedToApp)
                CurrentMeal.SaveToApp();
            SnapshotNeeded.IsPaused = true;
            // Wait for delayTime seconds or until a request to check immediately is received
            Task which = await Task.WhenAny(Task.Delay(delayTime * 1000), SnapshotNeeded.WaitWhilePausedAsync());
            if (!SnapshotNeeded.IsPaused)
                Utilities.DebugMsg($"In PeriodicSaveAsync SnapshotNeeded IsPaused is false");
        }
    }

    public static async Task<bool> GetRemoteMealListAsync()
    {
        try
        {
            return await RemoteWs.GetRemoteMealListAsync();
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            RemoteMealList.Clear(); // Something went wrong, better no list than a partial one
            return false;
        }
    }

    /// <summary>
    /// Read the list of meals stored in local storage (if there are any) and create MealSummary items from them
    /// If location access is permitted return the closest 
    /// </summary>
    /// <returns></returns>
    public static async Task GetLocalMealListAsync()
    {
        await StatusMsgAsync("Start analyzing local meal list");
        // Get the list of files by going through the Meal folder and remembering all the files called ...xml
        if (Directory.Exists(MealFolderPath))
        {
            // Make a list of bills, each one may have a corresponding image file with a related name.
            List<string> files = Directory.EnumerateFiles(MealFolderPath, "??????????????.xml")
                                 .Select(fp => Path.GetFileName(fp))
                                 .Where(fn => Regex.IsMatch(fn, @"\d{14}\.xml")) // 14 digits dot xml (yyyymmddhhmmss.xml)
                                 .OrderByDescending(fn => fn).ToList();
            await StatusMsgAsync($"Found {files.Count} candidate meal files");
            List<string> oldfiles = LocalMealList.Select(ms => ms.FileName).ToList(); // The order will have been determined the line above in a previous call 
            if (!Enumerable.SequenceEqual(files, oldfiles))
            {
                // The list of files has changed, so evaluate what's there now
                // First, the Meals which are now missing should be marked as not local and removed from the local list (they may still be in the remote list)
                Dictionary<string, string> newFilenames = files.ToDictionary(fn => fn);
                var missingList = LocalMealList.Where(ms => !newFilenames.ContainsKey(ms.FileName)).ToList(); // a separate list because we're changing LocalMealList
                foreach (var ms in missingList)
                {
                    ms.IsLocal = false;
                    LocalMealList.Remove(ms);
                }
                // What's left has a corresponding file
                Dictionary<string, MealSummary> existingLocalMs = LocalMealList.ToDictionary(ms => ms.FileName);
                Dictionary<string, MealSummary> existingRemoteMs = RemoteMealList.ToDictionary(ms => ms.FileName);
                // Iterate through the stored meals and create a  MealSummary object for each
                // The list of MealSummary objects is what is stored in LocalMealList, and it includes the presence of an image file if one exists
                foreach (var fileName in files.Where(fn => !existingLocalMs.ContainsKey(fn)))
                {
                    if (existingRemoteMs.TryGetValue(fileName, out var ms))
                    {
                        // The MealSummary is already in the RemoteMealList, so just mark it as local too and add it to LocalMealList
                        // because local meals are automatically backed up, this is the most common case
                        ms.IsLocal = true;
                    }
                    else
                    {
                        // This is a brand new Meal, not previously seen
                        ms = null;
                        Task<MealSummary> T = new Task<MealSummary>(() => MealSummary.LoadFromMealFile(fileName));
                        T.Start();
                        try
                        {
                            await T;
                            ms = T.Result;
                        }
                        catch (Exception ex)
                        {
                            var fileStream=File.Create(Path.Combine(Meal.MealFolderPath, fileName));
                            ReportCrash("Method", "GetLocalMealListAsync", fileStream, ex, fileName);
                        }
                        if (ms is null || ms.Size < 0) // it's a bad file
                            continue;
                    }
                    LocalMealList.Add(ms);
                }
            }
        }
        else // There's no folder for meals
            LocalMealList.Clear ();
        await StatusMsgAsync("Established local meal list");
    }

    /// <summary>
    /// Add new meals into the existing LocalMealList and store each one locally, used for archive restore
    /// </summary>
    /// <param name="newMeals">An enumerable list of Meal items</param>
    public static void AddLocalMeals(IEnumerable<Meal> newMeals, bool replace)
    {
        var localMealDict = new Dictionary<string, object>(); // the value is always null, it's the presence of the key that matters
        foreach(var ms in LocalMealList)
        {
            if (!ms.IsDefault)
                localMealDict.Add(ms.Id, null);
        }
        foreach (var meal in newMeals)
        {
            if (meal.Size < 0)
                continue; // This is a bad bill
            if (replace && localMealDict.ContainsKey(meal.Summary.Id))
            {
                LocalMealList.Remove(meal.summary);
                localMealDict.Remove(meal.summary.Id);
            }
            if (!localMealDict.ContainsKey(meal.Summary.Id)) 
            {
                if (!meal.IsDefault) // never save the default meal to persistent storage
                {
                    meal.SaveToFile();
                    meal.Summary.IsLocal = true; 
                }
                LocalMealList.Add(meal.summary);
                localMealDict.Add(meal.summary.Id, null); // to ensure we do not add duplicates
            }
        }
    }
    /// <summary>
    /// Select all but the latest meal for each venue from local storage, note that after calling this the MealListViewModel.SelectedMealSummariesCount will be wrong
    /// </summary>
    public static bool SelectOlder()
    {
        var list = LocalMealList.Where(ms => ms.IsLocal).OrderBy(ms => ms.VenueName).ThenByDescending(ms => ms.CreationTime);
        var distinctCount = list.DistinctBy(ms => ms.VenueName).Count();
        if (list.Count() == distinctCount)
            return false; // nothing to do
        string priorVenue = string.Empty;
        foreach (var ms in list)
            if (!(ms.FileSelected = priorVenue.Equals(ms.VenueName)))
                priorVenue = ms.VenueName;
        return true;
    }

    private static readonly ObservableCollection<MealSummary> localMealList = new ObservableCollection<MealSummary>();
    /// <summary>
    /// List of locally resident meals (though it is actually a list of meal summaries each representing a meal) in reverse order of creation
    /// time (so, newest first). Where a Meal is present both locally and remotely a reference to the same MealSummary is in both this and the remote list. 
    /// </summary>
    public static ObservableCollection<MealSummary> LocalMealList => localMealList;

    private static readonly ObservableCollection<MealSummary> remoteMealList = new ObservableCollection<MealSummary>();
    /// <summary>
    /// List of cloud resident meals (though it is actually a list of meal summaries each representing a meal) in reverse order of creation
    /// time (so, newest first). Where a Meal is present both locally and remotely a reference to the same MealSummary is in both this and the local list. 
    /// </summary>
    public static ObservableCollection<MealSummary> RemoteMealList => remoteMealList;
    #endregion
    #region Shared
    public override string ToString() => ToString(null);

    public string ToString(PersonCost personCost)
    {
        MemoryStream ms = new MemoryStream();
        TextToStream(ms, personCost);
        ms.Position = 0;
        StreamReader reader = new StreamReader(ms);

        string text = reader.ReadToEnd();
        return text;
    }

    void TextToStream(Stream stream, PersonCost personCost = null)
    {
        StreamWriter sw = new StreamWriter(stream);
        try
        {
            sw.WriteLine("DivisiBill " + Utilities.VersionName+"." + Utilities.Revision);
            #region Bill Properties
            if ((personCost is not null) && (personCost.Diner is not null))
                sw.WriteLine("Calculation for {0}", personCost.Diner.DisplayName);
            sw.WriteLine("Venue " + VenueName);
            sw.WriteLine("Created {0:F}", CreationTime);
            if (IsLastChangeTimeSet  && (LastChangeTime - CreationTime).Duration() > TimeSpan.FromSeconds(1))
                sw.WriteLine($"Updated {LastChangeTime:F}");
            sw.WriteLine("Tax Rate {0:P2}    Tip Rate {1:P0}\r\n", TaxRate, TipRate);
            #endregion
            #region Participant List and Amounts
            foreach (var pc in costs)
            {
                if (pc.Amount != 0)
                    sw.WriteLine("{0} {1, -40} {2,10:C}", (byte)(pc.DinerID) % 10,
                       pc.Diner is null ? pc.Nickname : pc.Diner.DisplayName,
                       pc.Amount);
            }
            if (IsAnyUnallocated)
                sw.WriteLine("{0, -42} {1,10:C}", "Unallocated", UnallocatedAmount);
            #endregion
            #region Item List
            sw.WriteLine();
            sw.Write("{0,10}  {1, -30} {2,10}", "Sharers", "Item", "Amount"); // Heading
            if (personCost is not null)
                sw.WriteLine(" {0,10}", "Share"); // Per person shares
            else
                sw.WriteLine();
            decimal dinerSubTotal = 0;
            foreach (var lineItem in lineItems)
            {
                for (int i = lineItem.SharedBy.Count - 1; i >= 0; i--)
                {
                    if (lineItem.SharedBy[i])
                        sw.Write((i + 1) % 10);
                    else
                        sw.Write(" ");
                }
                string lineItemText = lineItem.ItemName + (lineItem.Comped ? " (comped)" : "");
                sw.Write($"  {lineItemText, -30} {lineItem.Amount, 10:C}"); // add an extra space to make negative numbers line up 
                if (personCost is not null)
                {
                    decimal dinerAmount = lineItem.GetAmounts()[(int)personCost.DinerID - 1];
                    sw.Write($" {dinerAmount,10:C}");
                    dinerSubTotal += dinerAmount;
                }
                sw.WriteLine();
            }
            #endregion
            #region Per Bill Amounts and Totals
            sw.WriteLine();
            if (GetCompedAmount() != 0)
                sw.WriteLine("            {0, -30} {1,10:C}", "Comped", GetCompedAmount()); 
            if (GetCouponAmountBeforeTax() != 0)
                sw.WriteLine("            {0, -30} {1,10:C}", "Coupons", GetCouponAmountBeforeTax());
            sw.Write("            {0, -30} {1,10:C}", "Subtotal", SubTotal);
            if (dinerSubTotal != 0)
                sw.Write(" {0,10:C}", dinerSubTotal);
            sw.WriteLine();
            sw.WriteLine();
            sw.WriteLine("            {0, -30} {1,10:C}", "Tax", Tax);
            if (CouponAmountAfterTax != 0)
                sw.WriteLine("            {0, -30} {1,10:C}", "Discount After Tax", -CouponAmountAfterTax);
            sw.WriteLine("            {0, -30} {1,10:C}", "Tip", Tip);
            sw.WriteLine("            {0, -30} {1,10:C}", "Total", TotalAmount);
            #endregion
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            sw.WriteLine();
            sw.WriteLine("exception: {0}", ex.Message);
        }
        finally
        {
            sw.Flush();
        }
    }
    public async Task CreateEmailMessageAsync(PersonCost personCost = null)
    {
        List<string> recipients = new List<string>();
        if (personCost is null) // send it to everyone
        {
            foreach (var pc in Costs.Where(pc => !string.IsNullOrWhiteSpace(pc.Diner?.Email)))
                recipients.Add(pc.Diner.Email);
        }
        else // send it to just the one person
        {
            if (!string.IsNullOrWhiteSpace(personCost.Diner?.Email))
                recipients.Add(personCost.Diner.Email);
        }
        string body = ToString(personCost);
        var message = new EmailMessage
        {
            Subject = "DivisiBill sent you a bill",
            Body = body,
            To = recipients
        };
        if (!String.IsNullOrEmpty(VenueName))
            message.Subject += " from " + VenueName;

        string tempFilePath = null;
        // Attach image file
        if (HasImage)
            message.Attachments.Add(new EmailAttachment(ImagePath));
        // Attach a copy of the message in a text file to make it easier to read.
        var fn = "Bill-" + CreationTime.ToString("yyyyMMddHHmmss") + ".txt";
        tempFilePath = Path.Combine(FileSystem.AppDataDirectory, fn);
        File.WriteAllText(tempFilePath, body);
        message.Attachments.Add(new EmailAttachment(tempFilePath));
        // Send the message
        try
        {
            await Email.ComposeAsync(message);
        }
        catch (FeatureNotSupportedException fnsEx)
        {
            ReportCrash("ClassName", "Meal", null, fnsEx, FileName, "Email is not supported on this device");
        }
        catch (Exception ex)
        {
            ReportCrash("ClassName", "Meal", null, ex, FileName, "Email faulted");
        }
        // Now delete the temporary file used for attachment
        if (!string.IsNullOrWhiteSpace(tempFilePath))
            File.Delete(tempFilePath);
    }
    private void SetupChangedEvents()
    {
        foreach (var item in lineItems)
            ((LineItem)item).PropertyChanged += OnLineItemChange;
        lineItems.CollectionChanged += LineItems_CollectionChanged; // Will take care of any future additions and deletions from LineItems
        costs.CollectionChanged += Costs_CollectionChanged;
        Summary.PropertyChanged += Summary_PropertyChanged;
    }
    #endregion
    #region Load and Save

    /// <summary>
    /// Inspect the current bill and if it is old enough to be deemed a new bill
    /// rather than the continuation of an old one, then see if it is appropriate
    /// to save off the old version (meaning save it if it hasn't already been saved).
    /// </summary>
    private static async Task<bool> TrySaveOldBillAsync([CallerMemberName] string methodName = null, [CallerLineNumber] int callerLineNumber = 0)
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
    
    /// <summary>
    /// Creates a fake bill
    /// </summary>
    /// <param name="ms">The MealSummary to associate with this fake meal</param>
    /// <returns></returns>
    public static Meal LoadFake(MealSummary ms)
    {
        Meal m = new Meal() { Summary = ms };
        m.LoadFakeSettings();
        return m;
    }
    /// <summary>
    /// Creates a lot of fake bill data and flags the bill as being fake, there's a subtle difference between a default bill
    /// (the one we show if there are no other bills available) and a fake bill, which is one of the bills in the list of fakes
    /// that we make.
    /// </summary>
    private void LoadFakeSettings()
    {
        if (string.IsNullOrWhiteSpace(VenueName))
            VenueName = "Queasy Diner";
        CreationTime = DateTime.MinValue; // flag the bill as fake
        CreateFakeCosts();
        CreateFakeLineItems();
        taxRate = 0.0775;
        tipRate = 0.20;
        Frozen = true;
        FinalizeSetup();
    }

    public void FinalizeSetup()
    {
        SavedToApp = true;
        SavedToFile = true;
        SavedToRemote = true;
        MonitorChanges = true;

        UpdateAmounts();
        DistributeCosts();
        SetupChangedEvents();
    }

    public static void CreateFakeStoredBills()
    {
        // Note that they are added in order
        LocalMealList.Add(new MealSummary()
        {
            VenueName = "Fake McDonalds",
            CreationTime = new DateTime(2021, 1, 2, 3, 4, 5),
            RoundedAmount = 123
        });
        LocalMealList.Add(new MealSummary()
        {
            VenueName = "Fake California Pizza Kitchen",
            CreationTime = new DateTime(2010, 11, 12, 14, 43, 20),
            RoundedAmount = 234,
            FileSelected = true
        });
        LocalMealList.Add(new MealSummary()
        {
            VenueName = "Fake McDonalds",
            CreationTime = new DateTime(2010, 11, 11, 11, 11, 11),
            RoundedAmount = 456
        });
    }

    private void CreateFakeLineItems() => lineItems = new ObservableCollection<LineItem>()
        {
            new LineItem(){Amount =  20, SharesList = "111", ItemName = "Appetizer" },
            new LineItem(){Amount = -10, SharesList = "111", ItemName = "Discount" },
            new LineItem(){Amount =  30, SharesList = "001", ItemName = "Tasty Chicken" },
            new LineItem(){Amount =  40, SharesList = "100", ItemName = "Overdone Beef", Comped = true },
            new LineItem(){Amount =  60, SharesList = "210", ItemName = "Wine" },
            new LineItem(){Amount =  20, SharesList = "010", ItemName = "Fish & Chips" },
            new LineItem(){Amount =   5,                     ItemName = "Mystery item" },
        };

    private void CreateFakeCosts()
    {
        costs = new ObservableCollection<PersonCost>();
        for (int i = 0; i < 3; i++)
        {
            PersonCost pc = new PersonCost() { DinerID = (LineItem.DinerID)(i + 1), Diner = Person.AllPeople[i] };
            costs.Add(pc);
        }
    }

    protected bool MonitorChanges;
    public void MarkAsChanged()
    {
        if (!MonitorChanges)
            return;
        if (Frozen)
        {   // We're going to make an identical new bill from the same venue except the CreationTime will be now
            // We know there's already a persisted copy of the current bill (that's what 'Frozen' means 
            // However, the current bill MealSummary will be in the summary list, so stop using it and make a new one
            Frozen = false;
            MealSummary OriginalSummary = Summary;
            Summary = OriginalSummary.ShallowCopy(); // This will call MarkAsChanged, but this time Frozen will be false
            Summary.SnapshotStream = new MemoryStream(3000);
            UpdateOtherLists(); // Make sure we have appropriate Person and Venue entries to correspond with this bill
            CreationTime = DateTime.Now;
            ActualLastChangeTime = CreationTime; // because it has not been changed since creation
            // Because it is rarely used we do NOT inherit TipDelta values from frozen bills
            TipDelta = 0;
            if (HasImage)
            {
                // Copy the original image to the location expected by the new Summary, finding the original image is made
                // difficult by the fact that the CreationTime has been changed but we can use the original FileName
                if (string.IsNullOrEmpty(FileName))
                { 
                    if (Utilities.IsDebug) Debugger.Break(); 
                }
                else
                {
                    try
                    {
                        File.Copy(OriginalSummary.ImagePath, Summary.ImagePath);
                        Summary.DetermineHasImage();
                    }
                    catch (Exception)
                    {
                        if (Debugger.IsAttached) 
                            Debugger.Break();
                        // Not the end of the world if this fails, so just go on without it
                        Summary.DeleteImage();
                    }
                }
            }
            Summary.IsRemote = false;
            SaveToFile();
            Summary.Show();
        }
        else
            ActualLastChangeTime = DateTime.Now;
        if (SavedToApp)
            App.Settings.MealSavedToFile = false;
        SavedToApp = false;
        SavedToFile = false;
        SavedToRemote = false;
    }
    /// <summary>
    /// Mark the bill as being a new one and save the current state of it to disk if it has not already
    /// been saved.
    /// </summary>
    /// <param name="why">The reason why the bill is being declared a new one</param>
    /// <returns></returns>
    public async Task MarkAsNewAsync(string why, bool unconditional = false)
    {
        if (unconditional || OldEnoughToBeNewFile)
        {
            // See if the old version ought to be saved
            await Saver.SaveCurrentMealIfChangedAsync("MarkAsNew");
            // At this point the old version is saved and can no longer be changed, a copy of it still exists
            // in memory so we mark that copy to indicate why it exists, and that any change represents a new bill
            // and just wait for someone to change it (until then it can still be viewed).
            CreationReason = why;
            Frozen = true;  // Meaning it has been saved and now you have a new copy
        }
    }
    public string ApproximateAge => ApproximateAge(CreationTime);
    private TimeSpan Age => DateTime.Now - CreationTime;
    private TimeSpan IdleTime => DateTime.Now - LastChangeTime;
    public bool TooOldToContinue => IdleTime > App.MaximumIdleTime;
    public bool OldEnoughToBeNewFile => IdleTime > App.MinimumIdleTime;
    // Take the raw loaded bill and reconcile the PC entries in it with the people
    // we know about, recalculate amounts, and so on. After this the meal in memory
    // may differ from the stored one it was loaded from
    private void CompleteSetup()
    {
        DistributeCosts(); // Make sure the calculations are up to date
        SetupChangedEvents();
    }
    // Add anything from this meal that should be in other lists, most of the work is for the list of people,
    // but we may add a venue too. By the time this function is done there is a Person entry corresponding to
    // every PersonCost in Costs and a Venue entry for the bill venue name.
    public void UpdateOtherLists()
    {
        // Any known people (meaning recognized guids) will have already been linked, so handle what's left 
        // Add any missing people to the "Who" list, there are two cases here, one where a version of
        // DivisiBill was storing new guids for nicknames because it didn't know about the people we do.
        // In the other case the guid and nickname are legitimate, but the person record has been deleted.
        // Either way, we have an unused (by us) guid and a nickname, so we'll just make a new Person record
        var newPeople = new List<Person>();
        int nextNumber = 1;
        foreach (PersonCost personCost in Costs.Where(pc => pc.Diner is null)) // Rare case where the guid didn't correspond to a known person
        {
            if (Costs.Count(pc => pc.Nickname.Equals(personCost.Nickname)) > 1) // The same nickname is repeated, so make this one unique
            {
                personCost.Nickname += nextNumber; // Note the +=
                nextNumber++;
            }
            Person p = new Person(personCost.PersonGUID)
            {// Keep the guid in case we ever see it later
                Nickname = personCost.Nickname,
                LastName = Person.FromBill
            };
            personCost.Diner = p;
            newPeople.Add(p); // If it's an existing person this will just add an alias
        }
        if (newPeople.Count > 0)
        {
            Person.AddPeople(newPeople, replace: false);
            Task.Run(() => Person.SaveSettingsAsync()); // Fire and forget
        }
        // At this point the venue list might not contain the venue for this meal, just leave it that way
    }

    /// <summary>
    /// Read a meal from App local storage
    /// </summary>
    /// <returns></returns>
    public static Meal LoadFromApp()
    {
        string myString = App.Settings.StoredMeal;
        if (string.IsNullOrWhiteSpace(myString))
            DebugMsg("In Meal.LoadFromApp, no stored meal found");
        else
        {
            byte[] buf = Encoding.UTF8.GetBytes(myString);
            MemoryStream s = new MemoryStream(buf);
            Meal m = LoadFromStream(s);
            DebugMsg("in Meal.LoadFromApp meal = " + m.Summary);
            m.SavedToApp = true;
            m.SavedToFile = App.Settings.MealSavedToFile;
            m.Frozen = App.Settings.MealFrozen;
            m.Summary.IsLocal = File.Exists(m.Summary.FilePath);
            if (m.Summary.IsLocal)
                m.CheckImageFiles();
            m.SavedToRemote = App.Settings.MealSavedToRemote;
            m.Summary.IsRemote = m.SavedToRemote;
            m.MonitorChanges = true; // From now on take notice of changes
            return m;
        }
        return null;
    }
    // Create a crash report - this will be sent immediately (it doesn't wait for a program restart)
    public static void ReportCrash(String What, String Who, Stream sourceStream, Exception ex, string streamName, string errorDescription = "")
    {
        string errmsg = $"Meal.ReportCrash reported What={What}, Who={Who}, Exception={ex.ToString()}";
        Debug.WriteLine(errmsg);

        if  (!string.IsNullOrEmpty(errorDescription))
            errmsg += "\n" + errorDescription +"\n";

        ex.ReportCrash(errmsg, sourceStream, streamName);
    }
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
            m = (Meal)MealSerializer.Deserialize(sourceStream);
            if (ms is not null)
            {
                ms.RoundedAmount = m.Summary.RoundedAmount; // for the check in Summary.set
                m.Summary = ms; // Discard the one that was created as part of the deserialize operation in favor of the passed one 
            }
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
            foreach (var personCost in m.Costs.Where(pc => !pc.PersonGUID.Equals(Guid.Empty)))
            {
                personCost.SetDinerFromGuid();
            }
            if (setup)
                m.CompleteSetup();
            else
                m.DistributeCosts(); // Make sure the calculations are up to date
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
            m = new Meal() { CreationReason = ex.Message };
            m.Size = -1; // flag that we have no clue
            m.VenueName = "Bad bill";
        }
        return m;
    }
    // Move a suspect file into a different folder so it doesn't keep causing trouble
    public static void MoveSuspectFile(string TargetFileName)
    {
        string suspectFolderPath = Path.Combine(MealFolderPath, SuspectFolderName);
        Directory.CreateDirectory(suspectFolderPath);
        File.Move(Path.Combine(MealFolderPath, TargetFileName), Path.Combine(suspectFolderPath, TargetFileName));
    }
    private static Meal LoadFromSavedStream(MealSummary ms, bool setup = false)
    {
        LineItem.nextItemNumber = 1;
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
            m.CheckImageFiles();
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
            using (var sourceStream = File.OpenRead(Path.Combine(MealFolderPath, TargetFileName)))
            {
                LineItem.nextItemNumber = 1;
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
                    m.CheckImageFiles();
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
        }
        catch (FileNotFoundException)
        {
            // Could theoretically happen if someone is messing with the file system, so if it does, just return null
            return null;
        }
        return m;
    }
    public static async Task<Meal> LoadFromRemoteAsync(MealSummary ms, bool setup = false)
    {
        Meal m = null;
        using (Stream sourceStream = await RemoteWs.GetItemStreamAsync(RemoteWs.MealTypeName, ms.Id))
        {
            LineItem.nextItemNumber = 1;
            m = LoadFromStream(sourceStream, ms, setup);
            if (m is null || m.Size <= 0)
            {
                // The stream was bad so just return null
                Utilities.DebugMsg($"In Meal.LoadFromRemoteAsync: LoadFromStream returned null for {ms.Id}");
                m = null;
            }
            else
            {
                m.CheckImageFiles(); // Just in case it is stored locally
                m.Summary.IsRemote = true;
                m.SavedToRemote = true;
                m.MonitorChanges = true;
            }
        }
        return m;
    }
    public static async Task<Meal> LoadAsync(MealSummary ms, bool setup = false)
    {
        Meal m;
        if (ms.SnapshotValid)
            m = LoadFromSavedStream(ms, setup: setup);
        else if (ms.IsLocal)
            m = LoadFromFile(ms, setup: setup);
        else if (ms.IsRemote)
            m = await LoadFromRemoteAsync(ms, setup);
        else
            m = LoadFake(ms);
        return m;
    }
    /// <summary>
    /// Indicated that a current copy is saved to app storage
    /// </summary>
    private bool SavedToApp { get; set; }
    /// <summary>
    /// Indicates that a current copy is saved to local storage
    /// </summary>
    [XmlIgnore]
    public bool SavedToFile
    {
        get => savedToFile;
        private set => SetProperty(ref savedToFile, value, () => OnPropertyChanged(nameof(DiagnosticInfo)));
    }
    private bool savedToFile;

    /// <summary>
    /// Indicates that a current copy is saved to remote storage
    /// </summary>
    [XmlIgnore]
    public bool SavedToRemote
    {
        get => savedToRemote;
        private set => SetProperty(ref savedToRemote, value, () => OnPropertyChanged(nameof(DiagnosticInfo)));
    }
    private bool savedToRemote;
    private void SaveToApp()
    {
        Utilities.DebugMsg($"In Meal.SaveToApp");
        var buf = new byte[10000];
        MemoryStream s = new MemoryStream(buf);
        SaveToStream(s);
        string myString = Encoding.UTF8.GetString(buf, 0, (int)s.Position);
        if (Utilities.IsUWP && myString.Length > 4096) // too large to store on Windows
        {
            Utilities.DisplayAlertAsync("Error", $"Bill is too large ({myString.Length * 2} bytes) to store in App on Windows");
            myString = string.Empty;
        }
        App.Settings.StoredMeal = myString;
        App.Settings.MealFrozen = Frozen;
        App.Settings.MealSavedToRemote = SavedToRemote;
        App.Settings.MealSavedToFile = SavedToFile;
        SavedToApp = true;
        // No need to save the bill image if there is one, it is already in the private store 
    }
    public void SaveToStream(Stream streamParameter)
    {
        SaverVersion = Utilities.VersionName;
        DataVersion = "1.1"; // Increment when significant changes happen to the data format, like optional fields being added
        using (StreamWriter sw = new(streamParameter, Encoding.UTF8, -1, true))
        using (var xmlwriter = XmlWriter.Create(sw, new XmlWriterSettings() { Indent = true, OmitXmlDeclaration = true, NewLineOnAttributes = true }))
        {
            var namespaces = new XmlSerializerNamespaces();
            namespaces.Add(string.Empty, string.Empty);
            MealSerializer.Serialize(xmlwriter, this, namespaces);
        }
        DebugExamineStream(streamParameter);
    }
    public void SaveToSnapshot()
    {
        Stream s = Summary.SnapshotStream;
        if (s is null) // Enough memory for most (95%+) Meals - this should only be needed if the meal was the default and wasn't loaded from a stream
             s = Summary.SnapshotStream = new MemoryStream(3000);
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
                await Task.Run(() => SaveToFile());
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
    private void SaveToFile()
    {
        Debug.Assert(!(this == CurrentMeal && !IsLastChangeTimeSet), "Should not store an unchanged current meal");
        if (Costs.Count == 0 && LineItems.Count == 0) // This is an empty bill, do not store it
        {
            DebugMsg($"In Meal.SaveToFile: Bill {Summary.Id} is empty, ignoring it");
            SavedToFile = true; //don't bother trying again until it is changed
            return;
        }
        Directory.CreateDirectory(MealFolderPath);
        String TargetFilePath = FilePath;
        using (var stream = File.Open(TargetFilePath, FileMode.Create)) // Overwrites any existing file
        {
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
                App.Settings.MealSavedToFile = true;
        }
    }
    private async Task SaveToRemoteAsync()
    {
        if (Costs.Count == 0 && LineItems.Count == 0) // This is an empty bill, do not store it
        {
            DebugMsg($"In Meal.SaveToRemoteAsync: Bill {Summary.Id} is empty, ignoring it");
            SavedToRemote = true; //don't bother trying again until it is changed
            return;
        }
        if (Summary.SnapshotValid)
        {
            Summary.SnapshotStream.Position = 0;
            Size = Summary.SnapshotStream.Length;
            SavedToRemote = Summary.IsRemote = await RemoteWs.PutMealStreamAsync(Summary, Summary.SnapshotStream);
            if (this == CurrentMeal)
                App.Settings.MealSavedToRemote = true;
        }
    }

    [XmlIgnore]
    public MealSummary Summary
    {
        private set
        {
            if (value != summary)
            {
                if (summary is not null)
                {
                    summary.PropertyChanged -= Summary_PropertyChanged;
                    if (value is not null)
                    {
                        Debug.Assert(value.VenueName == summary.VenueName
                            && value.RoundedAmount == summary.RoundedAmount
                            && Utilities.WithinOneSecond(value.CreationTime, summary.CreationTime),
                            "A Summary replacement would change significant properties");
                    }
                }
                summary = value;
                summary.PropertyChanged += Summary_PropertyChanged;
                MarkAsChanged();
            }
        }
        get => summary ?? (summary = new MealSummary());
    }

    private void Summary_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
        if (e.PropertyName == nameof(MealSummary.IsLocal) || e.PropertyName == nameof(MealSummary.IsRemote))
            OnPropertyChanged(nameof(DiagnosticInfo));
        else if (e.PropertyName == nameof(MealSummary.HasImage))
            OnPropertyChanged(nameof(HasImage));
        else if (e.PropertyName == nameof(MealSummary.HasDeletedImage))
            OnPropertyChanged(nameof(HasDeletedImage));
        else if (e.PropertyName == nameof(MealSummary.CreationTime))
        {
            OnPropertyChanged(nameof(Age));
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(IsDefault));
        };
    }

    [XmlIgnore]
    public long Size
    {
        private set => Summary.Size = value;
        get => Summary.Size;
    }

    public string FileName => Summary.FileName;
    public string FilePath => Summary.FilePath;
    public string ImageName => Summary.ImageName;
    /// <summary>
    /// The fully qualified path to the bill image for this bill
    /// </summary>
    public string ImagePath => Summary.ImagePath;
    public bool HasImage => Summary.HasImage;
    public bool HasDeletedImage => Summary.HasDeletedImage;
    public void DeleteImage() => Summary.DeleteImage();
    public void TryUndeleteImage() => Summary.TryUndeleteImage();
    public bool ReplaceImage(string s) => Summary.ReplaceImage(s);
    public void CheckImageFiles()
    {
        Summary.DetermineHasImage();
        Summary.DetermineHasDeletedImage();
    }

    /// <summary>
    /// Called whenever a user tells us it's time to persist a file. This is a special action - it persists a snapshot of the
    /// current bill right now, but tries not to otherwise disturb the bill. We clone the 
    /// current Meal and work exclusively on the clone sidestepping any issues of where the current bill may be stored.
    /// </summary>
    public async Task SaveSnapshotAsync()
    {
        // First clone the Meal
        Meal m = LoadFromApp();
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
        m.Summary.IsLocal = false;
        m.Summary.IsRemote = false;
        if (HasImage)
            File.Copy(ImagePath, m.ImagePath, true);
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
#endregion
    #region Change Monitoring
    void OnLineItemChange(object sender, PropertyChangedEventArgs e)
    {
        MarkAsChanged();
        if (e.PropertyName.Equals(nameof(LineItem.Amount)) || e.PropertyName.Equals(nameof(LineItem.Comped)))
            UpdateAmounts();
        else if (e.PropertyName.Equals(nameof(LineItem.SharedBy)))
            IsDistributed = false;
    }

    void LineItems_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if ((e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add) ||
            (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace))
        { // Make sure the new items report any changes
            foreach (var item in e.NewItems)
                ((LineItem)item).PropertyChanged += OnLineItemChange;
        }
        if ((e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove) ||
            (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace))
        { // Make sure the old items no longer report any changes
            foreach (var item in e.OldItems)
                ((LineItem)item).PropertyChanged -= OnLineItemChange;
            if (LineItems.Count == 0)
                LineItem.nextItemNumber = 1;
        }
        MarkAsChanged();
        UpdateAmounts();
    }

    void Costs_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => MarkAsChanged();
    #endregion
    #region Data Items
    #region persistent items
    // Code to set VenueName asynchronously and maybe save the old version
    public async Task ChangeVenueAsync(string value)
    {
        Debug.Assert(!string.IsNullOrWhiteSpace(VenueName));
        Debug.Assert(!string.IsNullOrWhiteSpace(value));
        if (VenueName != value)
        {
            await MarkAsNewAsync("NewVenue"); // Flag for storage in a different location
            VenueName = value;
        }
    }

    [XmlElement(ElementName = "Restaurant")]
    public string VenueName
    {
        get => Summary.VenueName;
        set
        {
            if (VenueName != value)
            {
                Summary.VenueName = value;
                MarkAsChanged();
            }
        }
    }

    public string CreationReason; // changing this isn't worth saving the meal for

    public string SaveReason; // changing this isn't worth saving the meal for

    public string SaverVersion; // This is always set just before saving, so no need to monitor it

    public string DataVersion; // This is always set just before saving, so no need to monitor it

    // A few releases did not store a valid person Guid in Meal items, this indicates whether this meal was one of them 
    public bool PersonGuidsUseless => string.IsNullOrEmpty(SaverVersion) || SaverVersion[0] == '5';

    /// <summary>
    /// The curious layout of the xxxTime and ActualxxxTime properties is because we want to store the times accurately
    /// with time zone information but show them to the human as if they were all local times, so dinner in Mumbai and Dinner 
    /// in California both show as happening in the evening. Most people only ever operate in a single time zone, but for those
    /// that do not this seems like the least bad choice. More importantly, it means that the file name and the creation time
    /// align regardless of timezone.
    /// </summary>

    [XmlElement(ElementName = "CreationTime")]
    public string StoredCreationTime
    {
        get => ActualCreationTime.ToString("s", System.Globalization.CultureInfo.InvariantCulture) + ActualCreationTime.ToString("zzz", System.Globalization.CultureInfo.InvariantCulture);
        set => ActualCreationTime = DateTimeOffset.Parse(value);
    }
    [XmlIgnore]
    public DateTimeOffset ActualCreationTime
    {
        get => CreationTime;
        set => CreationTime = value.DateTime;
    }
    [XmlIgnore]
    public DateTime CreationTime
    {
        get => Summary.CreationTime;
        set
        {
            if (CreationTime != value)
            {
                // Since we are changing the CreationTime this will now look like a new local bill but we should not make it visible because it is changing
                if (!IsDefault)
                {
                    SavedToFile = false;
                    SavedToRemote = false;
                    Summary.IsLocal = true; // even if the prototype was a remote bill, the modified version is only local until such time as it is backed up
                    Summary.IsRemote = false;
                }
                Summary.CreationTime = value;
                MarkAsChanged();
            }
        }
    }

    /// <summary>
    /// The last time the Meal was changed - older meals (before 2022) will not have this value stored but it should always be present in newer ones. 
    /// </summary>
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

        set => ActualLastChangeTime = DateTimeOffset.Parse(value);
    }
    [XmlIgnore]
    public DateTimeOffset ActualLastChangeTime
    {
        get => Summary.ActualLastChangeTime;
        set
        {
            try
            {
                Summary.ActualLastChangeTime = value.DateTime;
            }
            catch (Exception)
            {
                // Just ignore it and leave the value alone
                Debugger.Break(); // break if there's a debugger attached   
                return;
            }
        }
    }

    [XmlIgnore]
    public DateTime LastChangeTime => Summary.LastChangeTime;

    public bool IsLastChangeTimeSet => Summary.IsLastChangeTimeSet;

    /// <summary>
    /// Indicates that this is a default bill, created as an example for a new user
    /// </summary>
    public bool IsDefault => Summary.IsDefault;

    public double TipRate
    {
        get => tipRate;
        set
        {
            // Do not simply test to see if there is no rate change because the Tip may still need to be modified
            if (tipRate != value || Tip != GetTip())
            {
                tipRate = value;
                Tip = GetTip(); 
                MarkAsChanged();
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Storage for TipDelta property
    /// </summary>
    private decimal tipDelta;

    /// <summary>
    /// The amount to add to (or subtract from) the calculated tip to get the actual amount
    /// charged, set manually, it is rarely large but is used to make up the difference when
    /// a specific amount (rather than a calculated percentage) is specified. It is reset to
    /// 0 whenever a bill is thawed because it's rarely the same (or used at all) on a new bill.
    /// </summary>
    [DefaultValue(typeof(decimal), "0")]
    public decimal TipDelta
    {
        get => tipDelta;
        set => SetProperty(ref tipDelta, value, () => { Tip = GetTip(); MarkAsChanged(); });
    }

    [XmlElement(ElementName = "TipOnTax")]
    public bool TipOnTax
    {
        get => tipOnTax;
        set => SetProperty(ref tipOnTax, value, () => { Tip = GetTip(); MarkAsChanged(); IsDistributed = false; });
    }


    /// <summary>
    /// <para>Set if coupon amounts are anyway, so if you had a $10 meal with a $1 discount you would pay
    /// tax on $9 normally.</para>
    /// <para>If this is set you pay tax on $10 and discount the result by $1.</para> 
    /// </summary>
    [XmlElement(ElementName = "TaxOnDiscount")]
    public bool IsCouponAfterTax
    {
        get => isCouponAfterTax;
        set => SetProperty(ref isCouponAfterTax, value, () => 
        {
            UpdateAmounts();
            MarkAsChanged();
            IsDistributed = false;
        });
    }

    public double TaxRate
    {
        get => taxRate;
        set => SetProperty(ref taxRate, value, () => { TaxDelta = 0; Tax = GetTax(); MarkAsChanged(); });
    }

    /// <summary>
    /// Storage for TaxDelta property
    /// </summary>
    private decimal taxDelta;

    /// <summary>
    /// The amount to add (or subtract) to the calculated tax to get the actual amount charged, set manually, 
    /// it should never be more than a few cents.
    /// </summary>
    [DefaultValue(typeof(decimal), "0")]
    public decimal TaxDelta
    {
        get => taxDelta;
        set => SetProperty(ref taxDelta, value, () => { Tax = GetTax(); MarkAsChanged(); });
    }

    private decimal scannedTax;
    [DefaultValue(typeof(decimal), "0")]
    public decimal ScannedTax
    {
        get => scannedTax;
        set => SetProperty(ref scannedTax, value, MarkAsChanged);
    }

    private decimal scannedSubTotal;
    [DefaultValue(typeof(decimal), "0")]
    public decimal ScannedSubTotal
    {
        get => scannedSubTotal;
        set => SetProperty(ref scannedSubTotal, value, MarkAsChanged);
    }

    public ObservableCollection<PersonCost> Costs
    {
        get => costs;
        set => SetProperty(ref costs, value, MarkAsChanged);
    }

    public ObservableCollection<LineItem> LineItems
    {
        get => lineItems;
        set => SetProperty(ref lineItems, value, MarkAsChanged);
    }
    #endregion
    #region public interface
    [XmlIgnore]
    public string DebugDisplay => "\"" + VenueName + "\"" + (IsDefault ? ", IsDefault" : $" at {CreationTime} {ApproximateAge} in {FileName}"); 
    public string DiagnosticInfo
    {
        get
        {
            StringBuilder info = new StringBuilder(Frozen ? "Frozen" : "Thawed", 100);
            if (Summary.IsLocal) info.Append(", IsLocal");
            if (SavedToFile) info.Append(", SavedToFile");
            if (Summary.IsRemote) info.Append(", IsRemote");
            if (SavedToRemote) info.Append(", SavedToRemote");
            if (HasImage) info.Append(", HasImage");
            if (HasDeletedImage) info.Append(", HasDeletedImage");
            return info.ToString();
        }
    }

    private bool frozen;
    /// <summary>
    /// Final values have been settled on for this bill, so any future attempt to change it is actually a 
    /// new bill (possibly for the same location) and should be given a new creation time. Any bill loaded from
    /// persistent storage starts out frozen
    /// </summary>
    [XmlIgnore]
    public bool Frozen
    {
        get => frozen;
        set => SetProperty(ref frozen, value, () => OnPropertyChanged(nameof(DiagnosticInfo)));
    }

    [XmlIgnore]
    public LineItem CutLineItem = null;

    [XmlIgnore]
    public PersonCost selectedPersonCost = null;

    private bool IsAnyUnallocated => UnallocatedAmount != 0;

    private decimal unallocatedAmount;
    [XmlIgnore]
    // The amount not allocated to any participant (so the sum of all the unallocated items). It's faintly possible
    // that a negative unallocated amount could offset a positive one but that's so unlikely it's not worth coding for.
    // Note that this is different from the Unshared amount.
    public decimal UnallocatedAmount
    {
        get => unallocatedAmount;
        private set => SetProperty(ref unallocatedAmount, value, () => { IsDistributed = false; });
    }

    public decimal GetUnallocatedAmount()
    {
        decimal d = 0;
        foreach (var item in lineItems)
        {
            bool isAllocated = false;
            foreach (var payee in item.SharedBy)
            {
                if (payee)
                {
                    isAllocated = true;
                    break;
                }
            }
            if (!isAllocated)
                d += Math.Abs(item.Amount);
        }
        if (d == 0) // No amounts unallocated
        {
            // Check for unusual case where discounts exceed costs
         decimal UnusedDiscount = GetOrderAmount() - GetRawCouponAmount()/(1M + (IsCouponAfterTax ? (decimal)TaxRate : 0));
            if (UnusedDiscount < 0)
                d = UnusedDiscount;
        }
        return d;
    }

    // Sum of the each person's rounded amounts (or rounded total if not all assigned)
    private decimal GetRoundedAmount()
    {
        decimal accumulatedTotal = 0;
        // Now step through the totals for each person and round it
        if (IsAnyUnallocated)
            accumulatedTotal = Math.Round(TotalAmount + 0.001M, 0); // MidpointRounding.AwayFromZero);
        else
            foreach (var costItem in Costs)
                accumulatedTotal += Math.Round(costItem.Amount + 0.001M, 0); // MidpointRounding.AwayFromZero);
        return accumulatedTotal;
    }

    private decimal subTotal;
    /// <summary>
    /// Subtotal is the sum of all the individual items except those that are comped
    /// </summary>
    [XmlIgnore]
    public decimal SubTotal
    {
        get => subTotal;
        private set => SetProperty(ref subTotal, value, UpdateAmounts);
    }

    /// <summary>
    /// The actual coupon amount applied to the bill, the sum of all the individual coupons. 
    /// </summary>
    private decimal GetRawCouponAmount()
    {
        decimal couponAmount = 0;
        foreach (var item in lineItems)
        {
            if (item.Amount < 0)
                couponAmount -= item.Amount; // Amount is negative, so couponAmount will be positive
        }
        return couponAmount;
    }

    /// <summary>
    /// The actual coupon amount applied to the bill, the sum of all the individual coupons 
    /// but no more than the sum of item costs less any comped items (so it is never negative)
    /// </summary>
    private decimal GetModifiedCouponAmount()
    {
        decimal subTotal = 0;
        decimal couponAmount = 0;
        foreach (var item in lineItems)
        {
            if (item.Amount < 0)
                couponAmount -= item.Amount; // Amount is negative, so couponAmount will be positive
            else if (!item.Comped)
                subTotal += item.Amount;
        }
        return Math.Min(subTotal, couponAmount);
    }

    /// <summary>
    /// The sum of all the individual coupons for the bill if they are applied before tax.
    /// </summary>
    public decimal GetCouponAmountBeforeTax()
    {
        return IsCouponAfterTax ? 0 : GetModifiedCouponAmount();
    }

    /// <summary>
    /// The sum of all the individual coupons for the bill if they are applied after tax.
    /// </summary>
    [XmlIgnore]
    [ObservableProperty]
    public partial decimal CouponAmountAfterTax { get; set; }

    /// <summary>
    /// Get the sum of all the individual coupons for the bill if they are applied after tax.
    /// </summary>
    private decimal GetCouponAmountAfterTax()
    {
        return IsCouponAfterTax ? GetModifiedCouponAmount() : 0;
    }

    private decimal GetCompedAmount() => lineItems.Where(item => item.Comped).Sum(item => item.Amount);

    /// <summary>
    /// This represents the amount against which Tip for a Meal ought to be calculated.
    /// Negative item amounts are simply discounts and are ignored when calculating a tip
    /// although they are used when calculating tax.
    /// </summary>
    /// <returns>Tip basis from the order items</returns>
    private decimal GetOrderAmount() => lineItems.Where(item => item.Amount > 0).Sum(item => item.Amount);

    /// <summary>
    /// The bill SubTotal - this should be the same number as is shown on the bill in ScannedSubtotal
    /// It is the sum of the item amounts ignoring any comped items.
    /// If discounts are taxable they do not affect the subtotal, refer to the <see cref="IsCouponAfterTax"/> property
    /// If discounts are NOT taxable (the normal case) they reduce the subtotal.
    /// Negative values are not allowed and return zero.
    /// </summary>
    /// <returns>Subtotal of items</returns>
    private decimal GetSubTotal() => Math.Max(0, 
        lineItems.Where(item => (item.Amount < 0 && !IsCouponAfterTax) // Coupon if applied pre-tax
            || (!item.Comped && item.Amount > 0)) // a simple entry for something that was purchased
            .Sum(item => item.Amount));

    /// <summary>
    /// The portion of the cost of a meal which is taxable, used to be complex, now it's just the subtotal
    /// </summary>
    private decimal TaxedAmount => SubTotal;

    private LineItem.DinerID amountForSharerID;

    // Set this to constrain amounts to a particular sharer
    [XmlIgnore]
    public LineItem.DinerID AmountForSharerID
    {
        get => amountForSharerID;
        set
        {
            if (amountForSharerID != value)
            {
                amountForSharerID = value;
                foreach (var li in lineItems)
                {
                    li.AmountForSharerID = amountForSharerID;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(LineItems));
            }
        }
    }

    [XmlIgnore]
    public decimal RoundedAmount
    {
        get => Summary.RoundedAmount;
        private set => Summary.RoundedAmount = value;
    }

    private decimal totalAmount;
    [XmlIgnore]
    public decimal TotalAmount
    {
        get => totalAmount;
        private set => SetProperty(ref totalAmount, value, () => { IsDistributed = false; });  // The grand total has changed, so the distribution must
    }

    public decimal GetTotalAmount() => SubTotal + Tax + Tip - CouponAmountAfterTax;

    // The basis on which the tip is calculated - the server does the work for comped items, and discounted ones,
    // so they are folded back in to the amount on which the tip is based
    private decimal GetTipBasis() => (GetOrderAmount() + (TipOnTax ? Tax : 0));

    private decimal tip;

    [XmlIgnore]
    public decimal Tip
    {
        get => tip;
        private set => SetProperty(ref tip, value, () => { TotalAmount = GetTotalAmount(); });
    }

    private decimal GetTip() => Math.Round(GetTipBasis() * (decimal)TipRate, 2) + TipDelta;

    public void SetRateFromTip(decimal value)
    {
        decimal tipBasis = GetTipBasis();
        if (tipBasis <= 0 || value <= 0)
            return;
        if (Math.Abs(Tip - value) >= 0.01M)
        { // recalculate rate based on new amount
            double newTipRate = SimplestRate(tipBasis, value, App.Settings.DefaultTipRate,100);
            TipDelta = value - Math.Round(tipBasis * (decimal)newTipRate, 2);
            Tip = value;
            TipRate = newTipRate;
        }
    }

    private decimal tax;

    /// <summary>
    /// Tax amount composed of tax calculated using TaxRate and an added TaxDelta to handle the case where simple arithmetic delivers a value different from the one in use
    /// </summary>
    [XmlIgnore]
    public decimal Tax
    {
        get => tax;
        private set => SetProperty(ref tax, value, () => { if (TipOnTax) Tip = GetTip(); TotalAmount = GetTotalAmount(); });
    }

    public decimal TaxWithoutDelta => Tax - TaxDelta;
    private decimal GetTax()
    {   // Most states specify simple rounding to do the calculation, and decimal.round does bankers rounding, what it says is:
        // "the calculated tax shall be rounded to a whole cent using a method that rounds up to the next cent whenever the third decimal place is greater than four"
        // See http://tax.ohio.gov/divisions/communications/information_releases/sales/st200505.stm for some examples
        double tax = (double)TaxedAmount * TaxRate;
        double cents = Math.Floor(tax * 100 + 0.5); // Round to nearest cent
        return (decimal)cents / 100 + TaxDelta;
    }

    public void SetRateFromTax(decimal value)
    {
        if (TaxedAmount <= 0 || value <= 0)
            return;
        else if (Math.Abs(Tax - value) >= 0.01M)
        {  // Recalculate the tax rate based on the new amount
            TaxRate = SimplestRate(TaxedAmount, value);
            TaxDelta = value - Tax;
        }
    }
 
    /// <summary>
    /// Return a simplified value describing the ratio between two numbers.
    /// We aim for a default rate if it is close enough, otherwise we just pick one.
    /// The definition of "close enough" is one rounded down to the nearest 1/4 % (one part in 400, or 0.0025)
    /// </summary>
    /// <param name="total">The total amount</param>
    /// <param name="part">The partial amount to be compared to it</param>
    /// <param name="defaultRate">
    ///     The preferred default ratio between the two numbers, if nothing is provided we use the default tax rate
    /// </param>
    /// <param name="lim">The granularity of the ratio to return, 100 means 1%, 400 means 1/4% and so on</param>
    /// <returns>A simplified ratio between the two values</returns>
    public static double SimplestRate(decimal total, decimal part, double defaultRate = double.NaN, int lim = 400)
    {
        Debug.Assert(lim > 0);
        Debug.Assert(total > 0);
        Debug.Assert(part > 0);
        try
        {
            total = Math.Abs(total);
            part = Math.Abs(part);
            if (double.IsNaN(defaultRate))
                defaultRate = App.Settings.DefaultTaxRate;
            decimal ratio = part / total;
            decimal defaultRateDelta = Math.Abs((decimal)defaultRate - ratio);
            if (defaultRateDelta * lim < 1) // Default tax rate is close enough
                return defaultRate;
            decimal roundedRatio = Math.Floor(ratio * lim) / lim; // round down to nearest 1/4 %
            decimal ratioDelta = ratio - roundedRatio;
            if (ratioDelta > 1M / (lim * 2M)) // it is closer to round up
            {
                roundedRatio += 1M / lim;
                ratioDelta = (1M / lim) - ratioDelta;
            }
            return (ratioDelta < defaultRateDelta) ? (double)roundedRatio : defaultRate;

        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// Calculate the various amounts (totals and percentages mostly) derived indirectly or directly from the list of items
    /// </summary>
    private void UpdateAmounts()
    {
        SubTotal = GetSubTotal();
        Tax = GetTax();
        Tip = GetTip();
        CouponAmountAfterTax = GetCouponAmountAfterTax();
        UnallocatedAmount = GetUnallocatedAmount();
        TotalAmount = GetTotalAmount();
    }
    #endregion
    #endregion
    #region Split up the bill amounts among all the diners
    [XmlIgnore]
    public bool IsDistributed { get; set; } = false;
    /// <summary>
    /// <para>Walk through all the Cost items (the participants) and allocate the appropriate share of the costs to each participant.</para>
    /// <para>Do this by allocating the cost of each item to the sharers for that item, then sharing the tax and tip amounts in proportion
    /// to the item based amount.</para>
    /// <para>This is really the core functionality of the program, distributing item costs, tax and tip
    /// between participants. It's pretty easy in the "normal" case of just a list of items shared out with tax and tip</para>
    /// <para>Some of the cases to handle include:</para>
    /// <list type="bullet">
    ///    <item><description>Tip on tax or not.</description></item>
    ///    <item><description>Taxable Coupons or not.</description></item>
    ///    <item><description>Coupons amount exceeds overall amount spent.</description></item>
    ///    <item><description>One or more participant coupon amounts exceed participant spend.</description></item>
    ///    <item><description>No participant spent anything.</description></item>
    ///    <item><description>Unallocated amount equals unallocated coupons.</description></item>
    /// </list>
    /// <para>Taxable discounts (which are rare) are handled by calculating what the discount before tax would have been and using 
    /// that in the calculations so we don't have to distribute it separately.</para> 
    /// <para>For any unused discount or error dues to rounding we share it between participants but try and keep participants with identical 
    /// costs the same amount so they'll end up with identical payments.</para>
    /// </summary>
    public void DistributeCosts(bool report = true)
    {
        #region Initial Evaluation and Tests
        if (Costs.Count == 0)
            return; // There's nobody to share with 
        #endregion
        #region Initialization
        PersonCost[] sharers = new PersonCost[LineItem.maxSharers];

        // Store a reference to each participants cost at their diner index 
        // in the sharers array to simplify the next step
        foreach (var personCost in Costs)
        {   // Note DinerIndex starts at 1
            sharers[personCost.DinerIndex] = personCost;
            personCost.ClearAllAmounts(); // Take this opportunity to clear out old data (even for irrelevant fields it simplifies debugging)
        }
        if (LineItems.Count == 0)
        {
            // As there are no people to share amongst we've done all that is necessary, just zero out a few things and exit
            RoundedAmount = 0;
            RoundingErrorAmount = 0;
            return;
        }
        #endregion
        #region Share out Items
        decimal unallocatedRunningTotal = 0;
        // Now step through all the line items, sharing out their cost
        foreach (var item in lineItems)
        {
            // Figure out what each person pays toward that item
            decimal[] amounts = item.GetAmounts();
            bool isUnallocated = true;
            // Now go though the sharers, allocating an amount to each
            for (int i = 0; i < LineItem.maxSharers; i++)
            {
                decimal amount = amounts[i];
                if (amount != 0)
                {
                    isUnallocated = false;
                    PersonCost pc = sharers[i]; // Find the cost item for sharer sharerInx
                    if (pc is null)
                    {
                        // This is an invalid meal, kludge it
                        pc = new PersonCost() { Nickname = "Unknown" + (i + 1).ToString(), DinerID = (LineItem.DinerID)(i + 1) };
                        sharers[i] = pc;
                        Costs.Add(pc);
                    }
                    if (amount < 0) 
                    {
                        pc.CouponAmount -= amount; // Amount is negative, so CouponAmount will be positive
                        // Notice that the comped flag is ignored on discounts
                    }
                    else if (item.Comped) // This item was comped, so the amount paid can be discounted
                    {
                        pc.CompedAmount += amount;
                        pc.OrderAmount += amount;
                    }
                    else // a simple share of item cost
                    {
                        pc.OrderAmount += amount;
                    }
                }
            } // end loop distributing shares
            if (isUnallocated)
                unallocatedRunningTotal += Math.Abs(item.Amount);
        }

        UnallocatedAmount = unallocatedRunningTotal;

        // Calculate the discount, if coupons are to be applied after tax, scale the coupon amount to a corresponding discount before tax
        decimal amountSum = 0, totalCouponAmount = 0;
        foreach (var costItem in Costs)
        {
            costItem.PreTaxCouponAmount = costItem.CouponAmount / (1M + (IsCouponAfterTax ? (decimal)TaxRate : 0));
            costItem.Discount = costItem.CompedAmount + costItem.PreTaxCouponAmount;
            costItem.UnusedCouponAmount = costItem.Discount;
            totalCouponAmount += costItem.PreTaxCouponAmount;
            amountSum += costItem.ChargedAmount; // CouponAmount not included
        }

        // Create a handy list of participants who spent something
        var costsWithOrderAmount = Costs.Where(pc => pc.OrderAmount > 0).ToList();
        if (costsWithOrderAmount.Count == 0)
        {
            // Trivial case, nobody bought anything so there's nothing to calculate, just mark as completed and return
            IsDistributed = true;
            return;
        }
        #endregion
        #region Ensure the Coupon Amount Does not Exceed the Overall Cost
        // This is a rare case (it means a large coupon in comparison to the bill, but nevertheless, if it
        // does happen, most venues will not give you money back (if they would, you effectively have money, 
        // not a coupon). If it does happen, prorate the individual coupons so the overall total ends up at zero
        decimal ExcessDiscount = Math.Max(0, totalCouponAmount - amountSum);
        if (ExcessDiscount > 0)
        {
            Tax = 0; // Because there will be no costs there can be no tax
            decimal ratio = amountSum / totalCouponAmount;
            foreach (var costItem in Costs.Where(pc => pc.PreTaxCouponAmount > 0))
            {  
                costItem.PreTaxCouponAmount *= ratio;
                costItem.UnusedCouponAmount = costItem.PreTaxCouponAmount;
                // prorate the coupon amount, but not the comped amount
                costItem.Discount = costItem.CompedAmount + costItem.PreTaxCouponAmount;
            }
        }
        #endregion
        #region Calculate Amount by Applying Discount (Coupon + Comp) Amounts to OrderAmount
        // In most bills the discount would have been completely consumed but...
        // There is an edge case where some people may have discounts which exceed their costs. However,
        // they are still on the hook for a tip, which may yet consume their remaining discount.
        // We'll reallocate any unused discount left after paying for a tip to other people, remember, the
        // discount has already been prorated so as not to exceed the sum of the paid (not comped) items.
        // We could be more methodical about this so as to distribute the extra discount according to
        // the shares specified by the user but this is such an unlikely case it hardly seems worthwhile.

        // First, work through all the costs that are in use consuming as much of the unused coupons
        // as possible noting what remains so it can be consumed by tip amounts later if possible.

        decimal remainingUnusedDiscount = 0;
        amountSum = 0;
        foreach (var costItem in Costs)
        {
            if (costItem.Discount <= costItem.OrderAmount)
            {
                // The normal case, where the discount is smaller than the participant's total cost
                costItem.Amount = costItem.OrderAmount - costItem.Discount;
                amountSum += costItem.Amount;
                costItem.UnusedCouponAmount = 0;
            }
            else
            {
                // The unusual case where the discount exceeds the cost 
                costItem.Amount = 0;
                // amountSum is unchanged
                costItem.UnusedCouponAmount -= costItem.ChargedAmount;
                remainingUnusedDiscount += costItem.UnusedCouponAmount;
            }
        }
        // In the normal case when we get to here Amount contains the taxable amount for each participant
        #endregion
        #region Apply Proportional Tax, Tip, and Discount To Each Cost 
        // Now step through the totals for each person that spent something and add in tax and tip.
        // Coupon amounts may, or may not be applied before tax, it is an option on each bill. In the rare case
        // where coupons are after-tax a calculated equivalent before-tax amount will have been applied.
        // Tax is shared in proportion to what was actually taxable (so if discounts were taxable, they count)
        // Tip is shared based on what is actually spent, so just because an item was discounted or comped,
        // you still get to tip on it

        decimal modifiedTaxRate = Tax == 0 ? 0 : Tax / TaxedAmount; // identical to TaxRate unless TaxDelta is set
        
        foreach (var costItem in costsWithOrderAmount) // So, just the people who bought things
        {
            decimal shareOfTax = (costItem.ChargedAmount - costItem.PreTaxCouponAmount) * modifiedTaxRate;
            // A little extra may be needed to restore the extra value of a post-tax coupon
            decimal shareOfTaxForCoupon = IsCouponAfterTax ? costItem.PreTaxCouponAmount * (decimal)TaxRate : 0; // Add a little extra if the coupon is taxed
            decimal shareOfTip = (costItem.OrderAmount + (TipOnTax ? shareOfTax + shareOfTaxForCoupon : 0)) * (decimal)TipRate;

            // At this point we can make a first estimate of what this participant owes ignoring unused discounts and rounding
            costItem.Amount += shareOfTax + shareOfTip;
            // In rare cases this participant will have some unused discount, if so, try and use it
            if (costItem.UnusedCouponAmount == 0)
            {
                // The normal case - we already consumed the discount for each participant, so there is nothing to do
            }
            else if (costItem.UnusedCouponAmount <= costItem.Amount)
            {
                // There's some unused discount, but it is less than the Amount for the participant
                costItem.Amount -= costItem.UnusedCouponAmount;
                remainingUnusedDiscount -= costItem.UnusedCouponAmount;
                costItem.UnusedCouponAmount = 0;
            }
            else
            {
                // The really rare case where the unused discount exceeds the remaining amount, this means
                // a different participant will pay part of this participant's share using their unused discount 
                costItem.UnusedCouponAmount -= costItem.Amount;
                remainingUnusedDiscount -= costItem.Amount;
                costItem.Amount = 0;
            }
        }
        // In the normal case when we get here all tax, tip and discounts have been applied and the Amount for each
        // participant represents what they should actually pay except that it is not yet rounded. 
        #endregion
        #region Get Handy List and Debug Remaining Discount
        // At this point, everyone has used up as much of their share of the discount as possible so we have
        // to use up the remainder. We'll just add it to the rounding error down below and distribute them together.

        // Get a list of just the people who still owe money because they can consume discount
        var costsWithAmount = Costs.Where(pc => pc.Amount > 0).ToArray();

        if (UnallocatedAmount == 0)
            Utilities.DebugAssert(remainingUnusedDiscount == 0, $"Excess discount {remainingUnusedDiscount:C} is unusual in {DebugDisplay}");
        #endregion
        #region Round all values
        // Until now we've been doing full accuracy calculations so as to minimize rounding errors
        // From this point onward, all the amounts are in exact dollars and cents so we have to handle them explicitly.
        foreach (var pc in costsWithOrderAmount)
            pc.RoundAllAmounts();
        amountSum = Math.Round(amountSum, 2);
        totalCouponAmount = Math.Round(totalCouponAmount, 2);
        decimal roundingErrorLeft = Math.Round(GetTotalAmount() - costsWithAmount.Sum(pc => pc.Amount), 2); // The difference between the bill total and sum of individual amounts
        #endregion
        #region Verify That Any Rounding Error is Small
        // Make the original rounding error visible so the UI can present it as a dire warning if it is large
        if (UnallocatedAmount == 0)
            RoundingErrorAmount = roundingErrorLeft + remainingUnusedDiscount; // This produces the actual rounding error regardless of unused discount 
        else    
            roundingErrorLeft = 0;
        /* At this point, there may be a few cents left over, caused by the difference between rounding individual totals 
         * after adding tax and tip and summing the results versus calculating the total, adding tax and tip, then rounding.
         * The difference is generally +/- one cent at most, but it could be as much as +/- one cent per person in theory and
         * for those odd bills where there is still some unused coupon amount it could be relatively large.
         * In the unusual case of large discount there may be more left but either way, we just share it out.
        */
        if (report && UnallocatedAmount == 0)
            Utilities.DebugAssert(Math.Abs(RoundingErrorAmount) <= (0.01m * Math.Max(1, costsWithAmount.Length)),
               $"in Meal.{nameof(DistributeCosts)}: {RoundingErrorAmount:C} unallocated after sharing costs in {DebugDisplay}");
        #endregion
        #region Share Out Any Rounding Error and, Rarely, Remaining Discount
        // Now ensure that if multiple participants had the same cost they pay the same amount because when what was purchased was the same but
        // the amounts owed are different, it tends to be noticeable so we try not to do that.
        if (Math.Abs(roundingErrorLeft) >= 0.01M)
        {
            // Group participants into lists with the same amount
            var amountClusters = costsWithAmount.Where(ci => ci.Amount > 0)
                .GroupBy(ci => ci.OrderAmount, (orderAmount, g) => new { OrderAmount = orderAmount, SameOrderAmountCount = g.Count(), CostsWithSameOrderAmount = g });

            if (Math.Abs(roundingErrorLeft) >= 0.02M)
            {
                // Step through each group with more than one member and see if there's enough to give all of them some 
                foreach (var cluster in amountClusters.Where(result => result.SameOrderAmountCount > 1))
                {
                    if (cluster.SameOrderAmountCount * 0.01M > Math.Abs(roundingErrorLeft))
                        continue; // Skip over groups with too many members to be able to share equally
                    decimal totalForCluster = cluster.CostsWithSameOrderAmount.Sum(costItem => costItem.Amount) + roundingErrorLeft;
                    decimal amountPerParticipant = Math.Truncate(totalForCluster * 100 / cluster.SameOrderAmountCount) / 100; // round down to the nearest penny
                    roundingErrorLeft = totalForCluster - amountPerParticipant * cluster.SameOrderAmountCount;
                    foreach (var costItem in cluster.CostsWithSameOrderAmount)
                        costItem.Amount = amountPerParticipant;
                } 
            }
            if (Math.Abs(roundingErrorLeft) >= 0.005m) // sharing among clusters of identical orders didn't do it, try giving it to any solo participant 
            {
                var ci = amountClusters.Where(result => result.SameOrderAmountCount == 1) // Just the ones with unique order amounts
                    .SelectMany(cluster => cluster.CostsWithSameOrderAmount) // Flatten the lists
                    .FirstOrDefault(ci => (ci.Amount + roundingErrorLeft) > 0); // now see if there's one that can handle the remainder 
                if (ci is not null)
                {
                    ci.Amount += roundingErrorLeft;
                    roundingErrorLeft = 0; // we consumed it 
                }
            }
        }
        if (roundingErrorLeft != 0) // As a last resort, just give it to the first participant that can handle it
        {
            // The extra is added(or subtracted from) the first non zero total it would not overwhelm.
            var costItem = costsWithAmount.FirstOrDefault(ci => (ci.Amount + roundingErrorLeft) > 0);
            if (costItem is not null)
                costItem.Amount += roundingErrorLeft;
            else // We could not find any way to allocate remainingTotal should always be zero
                Utilities.DebugMsg($"In Meal.{nameof(DistributeCosts)}: unable to eliminate rounding error {roundingErrorLeft:C} in {DebugDisplay}");
        }
        #endregion
        #region Final Calculations and Cleanup
        // Now that all the costs have been distributed to individuals, recalculate the rounded amount
        RoundedAmount = GetRoundedAmount();
        if (UnallocatedAmount == 0 && ExcessDiscount != 0)
            UnallocatedAmount = -ExcessDiscount; // To make it obvious to the user
        // And note that distribution is now accurate
        IsDistributed = true; 
        #endregion
    }
    /// <summary>
    /// The old version of the DistributeCosts Method
    /// </summary>
    public void DistributeCosts20230527()
    {
        static decimal[] GetAmounts(LineItem li)
        {
            decimal[] amounts = new decimal[LineItem.maxSharers];
            int centsLeft = (int)(100 * li.Amount); // Exact number of cents
            int howMany = li.TotalShares;
            if (howMany > 0)
            {
                // Figure out what each person pays toward that item
                int eachShare = centsLeft / howMany;
                // Now go though the sharers, allocating an amount to each
                for (int i = 0; (i < LineItem.maxSharers) && (howMany > 0); i++)
                {
                    if (li.SharedBy[i])
                    {
                        int shares = 1 + li.ExtraShares[i];
                        howMany -= shares;
                        decimal amount;
                        if (howMany > 0)
                        {
                            amount = (decimal)((double)(eachShare * shares) / 100);
                            centsLeft -= eachShare * shares;
                        }
                        else
                            amount = (decimal)((double)centsLeft / 100); // Last person gets the remainder
                        amounts[i] += amount;
                    }
                } // end loop distributing shares
            }
            return amounts;
        }

        UnallocatedAmount = GetUnallocatedAmount();
        if (Costs.Count == 0)
            return; // There's nobody to share with

        PersonCost[] sharers = new PersonCost[LineItem.maxSharers];

        // Store a reference to each participants cost at their diner index 
        // in the sharers array to simplify the next step
        foreach (var item in Costs)
        {   // Note DinerIndex starts at 1
            sharers[item.DinerIndex] = item;
            item.OrderAmount = 0;
            item.Amount = 0;
            item.Discount = 0;
            item.CompedAmount = 0;
        }
        if (LineItems.Count == 0)
        {
            // As there are no people to share amongst we've done all that is necessary, just zero out a few things and exit
            RoundedAmount = 0;
            RoundingErrorAmount = 0;
            return;
        }
        // Now step through all the line items, sharing out their cost
        foreach (var item in lineItems)
        {
            // Figure out what each person pays toward that item
            decimal[] amounts = GetAmounts(item);
            // Now go though the sharers, allocating an amount to each
            for (int i = 0; i < LineItem.maxSharers; i++)
            {
                decimal amount = amounts[i];
                if (amount != 0)
                {
                    PersonCost pc = sharers[i]; // Find the cost item for sharer sharerInx
                    if (pc is null)
                    {
                        // This is an invalid meal, kludge it
                        pc = new PersonCost() { Nickname = "Unknown" + (i + 1).ToString(), DinerID = (LineItem.DinerID)(i + 1) };
                        sharers[i] = pc;
                        Costs.Add(pc);
                    }
                    if (item.Comped) // This item was comped, so the amount paid can be discounted
                    {
                        pc.CompedAmount += amount;
                        pc.Discount += amount;
                        pc.OrderAmount += amount; // Because we tip on comped items
                    }
                    else if (amount < 0) // This is a discount, so remember it for tax calculation
                        pc.Discount -= amount;
                    else
                    {
                        pc.Amount += amount;
                        pc.OrderAmount += amount;
                    }
                }
            } // end loop distributing shares
        }
        // At this point we have been through all the line items and added up the totals for each person
        // There is an edge case where some people may have discounts which exceed their costs, if they
        // do we'll reallocate their unused discount to other people evenly. We could be more methodical
        // about this so as to distribute the extra discount according to the shares specified by the user
        // but this is such an unlikely case it hardly seems worthwhile.

        // First, we get a list of the costs that were exceeded by a discount and zero them, adding up the unused discount.
            decimal excessDiscount = 0;
        foreach (var costItem in Costs.Where(pc => pc.Discount > pc.OrderAmount))
        {
            excessDiscount += costItem.Discount - costItem.OrderAmount;
            costItem.Discount = costItem.OrderAmount;
        }

        // Now it's possible to get a list of just the people who spent money
        var nonZeroCosts = Costs.Where(pc => pc.OrderAmount > pc.Discount).ToArray();

        // if necessary, share out the extra discount 
        if (excessDiscount > 0 && nonZeroCosts.Length > 0)
        {
            int remainingCosts = nonZeroCosts.Length; // How many have not yet been given a share
            // Iterate through the people who have some cost left, sharing out the remaining discounts as evenly as possible.
            // Do the smallest ones first so as to be sure to use up all the excess discount in a single pass 
            foreach (var costItem in nonZeroCosts.OrderBy(pc => pc.OrderAmount - pc.Discount))
            {
                var extraDiscount = costItem.OrderAmount - costItem.Discount; // Each person gets no more discount than they spent
                extraDiscount = Math.Min(extraDiscount, excessDiscount / remainingCosts); // and no more than a share of what's left
                costItem.Discount += extraDiscount;
                excessDiscount -= extraDiscount;
                remainingCosts--;
            }
            Debug.Assert(excessDiscount >= 0, "Excess discount is negative and it shouldn't ever be");
        }

        // Now step through the totals for each person that spent something add in tax and tip, do not tax discounts
        // Tax is shared in proportion to what was actually taxable (so if coupons were taxable, they count)
        // Tip is shared based on what is actually spent, so even though a meal may be discounted, you still get to tip on all of it
        decimal remainingTotal = TotalAmount;
        decimal TipBasis = GetOrderAmount();

        foreach (var costItem in Costs.Where(pc => pc.OrderAmount > 0)) // So, just the people who bought things
        {
            decimal taxableAmount = Math.Max(0, costItem.ChargedAmount
                - (IsCouponAfterTax ? 0 : (costItem.Discount - costItem.CompedAmount))); // Comped items are included in discount number
            decimal shareOfTax = TaxedAmount > 0 ? Tax * taxableAmount / TaxedAmount : 0;
            decimal shareOfTip = TipBasis > 0 ? Tip * costItem.OrderAmount / TipBasis : 0;
            decimal shareOfSubtotal = costItem.OrderAmount - costItem.Discount;

            costItem.Amount = Math.Round(shareOfSubtotal + shareOfTax + shareOfTip, 2);
            remainingTotal -= costItem.Amount;
        }
        RoundingErrorAmount = Math.Round(remainingTotal, 2);
        /* At this point, there may be a few cents left over, caused by the difference between rounding individual totals 
         * after adding tax and tip and summing the results versus calculating the total, adding tax and tip, then rounding.
         * The difference is generally +/- one cent at most, but it could be as much as +/- one cent per person in theory.  
         * The extra is added (or subtracted from) the first non zero total it would not overwhelm.
        */
        if (IsUnsharedAmountSignificant && nonZeroCosts.Length > 0)
        {
            Utilities.DebugMsg($"In {nameof(DistributeCosts20230527)} : {remainingTotal:C} was unallocated after sharing costs in {DebugDisplay}");
            var costItem = nonZeroCosts.First(ci => (ci.Amount + remainingTotal) > 0);
            costItem.Amount += remainingTotal;
        }
        // Now that all the costs have been distributed to individuals, recalculate the rounded amount
        RoundedAmount = GetRoundedAmount();
    }

    internal decimal CompareCostDistribution(bool report = true)
    {
        decimal totalDifference = 0;
        DistributeCosts20230527();
        string s =  JsonSerializer.Serialize<List<PersonCost>>(Costs.ToList());
        List<PersonCost> OldCosts = JsonSerializer.Deserialize<List<PersonCost>>(s);
        DistributeCosts();
        foreach ((PersonCost oldPc, PersonCost newPc) in OldCosts.Zip(Costs)) 
        {
            if (oldPc is null || newPc is null) break;
            totalDifference += Math.Abs(oldPc.Amount - newPc.Amount);
        }
        if (report && totalDifference > 0.25m && UnallocatedAmount == 0)
            Utilities.DebugMsg($"In Meal.CompareCostDistribution: Distribution difference of {totalDifference:C} detected in {DebugDisplay}");
        return totalDifference;
    }
    #endregion
    #region Clearing and restoring the list of items
    private readonly List<LineItem> savedLineItems;
    private uint savedNextItemNumber;

    public bool CanClearLineItems => (LineItems.Count > 1) || ((LineItems.Count > 0) && (LineItems[0].Amount > 0));

    public bool CanUndoClearLineItems => savedLineItems.Count != 0;

    public void UndoClearLineItems()
    {
        if (savedLineItems.Count != 0)
        {
            LineItems.Clear();
            foreach (var item in savedLineItems)
                LineItems.Add(item);
            savedLineItems.Clear();
            LineItem.nextItemNumber = savedNextItemNumber;
            // Now make sure all the diners still exist
            var dinerIndexValid = new bool[LineItem.maxSharers];
            foreach (var item in Costs)
                dinerIndexValid[item.DinerIndex] = true;
            foreach (var item in LineItems)
            {
                for (int i = 0; i < LineItem.maxSharers; i++)
                {
                    if (item.SharedBy[i] && !dinerIndexValid[i])
                        item.SharedBy[i] = false;
                }
            }
        }
    }

    public bool ClearLineItems()
    {
        if (LineItems.Count > 1 || (LineItems.Count == 1 && LineItems[0].Amount != 0))
        {
            savedLineItems.Clear();
            foreach (var item in lineItems)
                savedLineItems.Add(item);
            LineItems.Clear();
            savedNextItemNumber = LineItem.nextItemNumber;
            LineItem.nextItemNumber = 1;
            return true;
        }
        return false;
    }
    #endregion
    #region Clearing and restoring the list of diners with costs
    private List<PersonCost> savedCosts;

    public bool CanUndoCosts => savedCosts is not null && savedCosts.Count > 0;

    public static readonly string MealFolderPath = Path.Combine(App.BaseFolderPath, MealFolderName);
    public static readonly string ImageFolderPath = Path.Combine(App.BaseFolderPath, ImageFolderName);
    public static readonly string TempImageFilePath = Path.Combine(App.BaseFolderPath, ImageFolderName, "NewImage.jpg");
    public static string DeletedItemFolderPath = Path.Combine(App.BaseFolderPath, DeletedItemFolderName);

    private decimal unsharedAmount;

    /// <summary>
    /// This is the 'smush' left over when all the costs have been allocated to participants. It is supposed to be a few cents 
    /// at most caused by inevitable rounding errors as we share items amongst participants. We generally only expose it to the
    /// user when it is too large, indicating there is some sort of calculation problem. 
    /// </summary>
    [XmlIgnore]
    public decimal RoundingErrorAmount
    {
        get => unsharedAmount;
        private set
        {
            SetProperty(ref unsharedAmount, value);
            IsUnsharedAmountSignificant = value != 0 && !IsAnyUnallocated && value * 100 > (Costs.Count(pc => pc.Amount > 0) + 1);
        }
    }

    private bool isUnsharedAmountSignificant;

    [XmlIgnore]
    // The UnsharedAmount is too large indicating there is some sort of calculation problem.
    public bool IsUnsharedAmountSignificant
    {
        get => isUnsharedAmountSignificant;
        private set => SetProperty(ref isUnsharedAmountSignificant, value);
    }


    public void UndoCosts()
    {
        // The costs list is, by design, stored in DinerIndex order and consequently, so is the savedCosts list
        // So, iterate through savedCosts from last to first and you can make one pass through Costs
        // replacing or inserting items as needed

        if (CanUndoCosts)
        {
            int costInx = Costs.Count - 1; // Last element

            if (costInx < 0)
            {
                // costs list is empty, the merge is trivial
                foreach (var pc in savedCosts)
                    Costs.Add(pc);
            }
            else
            {
                for (int savedCostInx = savedCosts.Count - 1; savedCostInx >= 0; savedCostInx--)
                {
                    var pc = savedCosts[savedCostInx];
                    while ((costInx >= 0) && (Costs[costInx].DinerID > pc.DinerID))
                    {
                        costInx--;
                    }
                    if ((costInx >= 0) && (Costs[costInx].DinerID == pc.DinerID))
                    {
                        // Oh dear, the CostIndex has been reused, so do not replace this one if it is in use
                        var newpc = Costs[costInx];
                        if (newpc.Amount == 0) // If the amount is zero, perhaps no items refer to it
                        {
                            bool shared = false;
                            foreach (var item in lineItems)
                            {
                                if (item.SharedBy[newpc.DinerIndex])
                                {
                                    shared = true;
                                    break;
                                }
                            }
                            if (!shared) // nobody is sharing this one, add it to the list of ones we can remove
                            {
                                Costs.RemoveAt(costInx); // Remove the diner with the same DinerIndex
                                Costs.Insert(costInx, pc); // put the new diner in the same place
                            }
                        }
                    }
                    else
                        Costs.Insert(costInx + 1, pc); // Insert the new diner after the one with a smaller DinerIndex
                }
            }
            // Now throw away the saved ones
            savedCosts.Clear();
        }
    }

    public void ClearCosts()
    {
        var newSavedCosts = new List<PersonCost>();
        foreach (var pc in Costs)
        {
            if (pc.Amount == 0) // If the amount is zero, perhaps no items refer to it
            {
                bool shared = false;
                foreach (var item in lineItems)
                {
                    if (item.SharedBy[pc.DinerIndex])
                    {
                        shared = true;
                        break;
                    }
                }
                if (!shared) // nobody is sharing this one, add it to the list of ones we can remove
                    newSavedCosts.Add(pc);
            }
        }
        if (newSavedCosts.Count > 0)
        {
            foreach (var pc in newSavedCosts)
                Costs.Remove(pc);
            savedCosts = newSavedCosts;
        }
    }

    public PersonCost GetNextPersonCost(PersonCost currentPc)
    {
        return currentPc is null ? Costs.FirstOrDefault() : Costs.SkipWhile(pc => pc != currentPc).Skip(1).FirstOrDefault();
    }
    #endregion
    #region Manipulating cost list

    /// <summary>
    /// Calculates a set of values proportionate to the values passed in the amounts array, these values are is determined heuristically
    /// meaning a more accurate approximation may exist, but this function will stop when it finds one that is "good enough".
    /// 
    /// Negative amounts are basically ignored.
    /// </summary>
    /// <param name="amounts">Array of individual amounts assigned to each array index</param>
    /// <returns>An array of share values from 0 to 9 to approximate the proportions in the amounts array</returns>
    public static byte[] CostsToShares(decimal[] amounts)
    {
        double totalAmount = 0, maxAmount = 0;

        foreach (var a in amounts)
        {
            if (a > 0)
            {
                totalAmount += (double)a;
                maxAmount = Math.Max(maxAmount, (double)a);
            }
        }

        byte[] bestShares = new byte[amounts.Length];
        double bestDifference = double.MaxValue;
        foreach (int maxShares in new List<int>() { 8, 9, 7, 6, 5 }) // 4 and 2 are equivalent to 8, 3 is equivalent to 6
        {
            byte[] shares = new byte[amounts.Length];
            double difference = 0;
            double shareAmount = maxAmount / maxShares; // So maxShares are sufficient to represent the maximum cost
                                                        // This means nobody will have more than maxShares
            for (int i = 0; i < amounts.Length; i++)
            {
                if (amounts[i] > 0)
                {
                    shares[i] = (byte)Math.Round((double)amounts[i] / shareAmount);
                    difference += Math.Pow((double)(shareAmount * shares[i] - (double)amounts[i]), 2);
                }
            }
            if (difference < bestDifference) // This new value is the best so far
            {
                bestDifference = difference;
                bestShares = shares;
            }

            if (difference < (totalAmount / 10000)) // This is our definition of "good enough"
                break;
        }

        // At this point we have our best guess share proportions, but not necessarily in the simplest form, so fix that
        SimplifyShares(bestShares);

        return bestShares;
    }

    /// <summary>
    /// Takes a list of shares in a byte array and removes any common factors so they are in as simple a form as possible.
    /// For example 8:4:2 would be simplified to 4:2:1.
    /// </summary>
    /// <param name="shares">An array of individual share values (positive integers or zero)</param>
    public static void SimplifyShares(byte[] shares)
    {
        int gcd(int a, int b) // Greatest Common Divisor - look it up 
        {
            if (a == 0)
                return b;
            return gcd(b % a, a);
        }

        int GCD = 0;

        // Find the GCD
        foreach (byte i in shares.Where(i => i > 0))
            GCD = gcd(GCD, i);
        // Divide each share amount by it, to get them in the lowest common denominator
        for (int i = 0; i < shares.Length; i++)
        {
            if (shares[i] > 0)
            {
                shares[i] = (byte)(shares[i] / GCD);
            }
        }
    }

    // Take the PersonCost in pc and give it the new DinerID and place the
    // former data cost item for the new DinerID in the old DinerID slot 
    public void AssignDinerID(PersonCost pc, LineItem.DinerID newDinerID)
    {
        LineItem.DinerID oldDinerID = pc.DinerID;
        // Iterate through the items, moving the sharers from old to new entry
        foreach (var costItem in LineItems)
            costItem.SwapSharerID(newDinerID, oldDinerID);
        // Find if a PersonCost used to use this DinerID and if so give it the ID from this one
        PersonCost oldpc = Costs.FirstOrDefault(item => item.DinerID == newDinerID);
        if (oldpc is not null)
            oldpc.DinerID = pc.DinerID;
        pc.DinerID = newDinerID;
    }

    // Remove a specific PersonCost from the list 
    public void CostListDelete(PersonCost pc)
    {
        if (Costs.Count == 0)
            return;
        //Now remove that diner from any items
        LineItem.DinerID dinerID = pc.DinerID;
        // Costs is sorted by DinerID, so just look in the right place
        foreach (var item in LineItems)
        {
            if (item.GetShares(dinerID) > 0)
                item.SetShares(dinerID, 0);
        }
        // Now the diner has been removed from all items it is safe to delete
        Costs.Remove(pc);
        DistributeCosts();
    }
    public void CostListDeleteAll()
    {
        if (Costs.Count == 0)
            return;
        foreach (var li in LineItems)
            li.DeallocateShares();
        Costs.Clear();
    }


    public PersonCost CostListAdd(Person p)
    {
        if (Frozen) // If this is an untouched bill it might need reordering to eliminate gaps in cost numbers
            CostListResequence();
        if (Costs.Count >= LineItem.maxSharers)
            return null;
        foreach (var item in CurrentMeal.Costs)
        {
            if (item.Diner == p)
                return null;
        }
        LineItem.DinerID availDinerID = (LineItem.DinerID)((int)LineItem.DinerID.first + Costs.Count);
        // Allocate a new item, populate it, and add it to the list
        var pc = new PersonCost() { DinerID = availDinerID, Diner = p };
        Costs.Insert(pc.DinerIndex, pc);
        return pc;
    }
    /// <summary>
    /// Loops sending copies of Meals to the Cloud. 
    /// </summary>
    /// <returns></returns>
    #endregion
    #region Cloud Backup
    // Also see Saver.RemoteLoop
    private static Task BackupTask = null;
    private static readonly CancellationTokenSource BackupCancellationTokenSource = new CancellationTokenSource();
    private static readonly AwaitableQueue<MealSummary> backupQueue = new AwaitableQueue<MealSummary>();
    internal static void StartBackupToRemote()
    {
        if (BackupTask is null)
            BackupTask = BackupToRemoteAsync(BackupCancellationTokenSource.Token);
    }
    internal static async Task StopBackupToRemoteAsync()
    {
        if (BackupTask is null)
            return;
        Task OldBackupTask = BackupTask;
        BackupTask = null;
        BackupCancellationTokenSource.Cancel();
        await OldBackupTask;
    }
    /// <summary>
    /// First figure out whether there are any meals that are local, but not remote
    /// put each of them (more precisely, their MealSummary) in a queue to be transmitted
    /// then enter a loop sending each meal and removing it from the queue. In the meantime
    /// the main process may add additional meals to the queue as they are saved (by calling
    /// QueueForBackup).
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private static async Task BackupToRemoteAsync(CancellationToken cancellationToken)
    {
        Utilities.DebugMsg("Entered BackuptoRemoteAsync, waiting for CloudAllowedSource");
        await App.CloudAllowedSource.WaitWhilePausedAsync();
        Utilities.DebugMsg("In BackuptoRemoteAsync, CloudAllowedSource no longer paused");
        // This is where all the elapsed time goes, reaching out over the network
        List<RemoteItemInfo> remoteFileInfoList = await RemoteWs.GetItemInfoListAsync(RemoteWs.MealTypeName);
        // We use HashSet types to store the data, but the performance difference pales compared to the network time above        
        HashSet<string> remoteMealNames = [.. remoteFileInfoList.Select(x => x.Name)];
        await App.InitializationComplete.Task; // Wait until LocalMealList is established
        var remoteFileInfoDict = remoteFileInfoList.ToDictionary(m => m.Name);
        HashSet<string> localMealNames = new ();
        foreach (var ms in LocalMealList)
        {
            if (remoteMealNames.Contains(ms.Id))
                ms.IsRemote = true;
            localMealNames.Add(ms.Id);
        }
        // Queue each MealSummary that is not remote for transmission
        HashSet<string> localOnlyMealNames = new(localMealNames);
        localOnlyMealNames.ExceptWith(remoteMealNames);
        foreach (string mealName in localOnlyMealNames)
        {
            MealSummary ms = LocalMealList.First(foundMs => mealName.Equals(foundMs.Id));
            if (!ms.IsFake)
                backupQueue.Enqueue(ms);
        }
        // Start the actual transmission process - it will loop forever sending each MealSummary in the queue
        await BackupLoopAsync(cancellationToken);
    }
    /// <summary>
    /// Download all the remote meals that have valid content (meaning they can be deserialized) and are
    /// not stored locally.
    /// </summary>
    /// <param name="cancellationToken">Set this to cancel the operation </param>
    /// <returns>A task to track the status of the operation.</returns>
    /// <exception cref="OperationCanceledException">Thrown if the task is canceled</exception>
    public static async Task RecoverFromRemoteAsync(CancellationToken cancellationToken, Action<int,int,int> ReportProgress)
    {
        DebugMsg("Entered RecoverFromRemoteAsync, waiting for CloudAllowedSource");
        await App.CloudAllowedSource.WaitWhilePausedAsync();
        DebugMsg("In RecoverFromRemoteAsync, CloudAllowedSource no longer paused");
        List<RemoteItemInfo> remoteFileInfoList = await RemoteWs.GetItemInfoListAsync(RemoteWs.MealTypeName);
        var remoteMealNames = remoteFileInfoList.Select(x => x.Name);
        await App.InitializationComplete.Task; // Wait until LocalMealList is established
        // Get a dictionary of local file names
        var localMealDict = new Dictionary<string, MealSummary>();
        await GetLocalMealListAsync();
        foreach (var ms in LocalMealList)
            localMealDict.Add(ms.Id, ms);
        // Get a list of remote only files (quite inefficient but that probably doesn't matter)
        var remoteOnlyFileInfoList = remoteFileInfoList.Where(rfi => !localMealDict.ContainsKey(rfi.Name)).ToList();
        int totalFiles = remoteOnlyFileInfoList.Count, filesWithoutError = 0, filesInError = 0, costMismatches = 0;
        decimal totalDifference = 0;
        ReportProgress(totalFiles, filesWithoutError, filesInError);
        foreach (var rfi in remoteOnlyFileInfoList)
        {
            using (Stream sourceStream = await RemoteWs.GetItemStreamAsync(RemoteWs.MealTypeName, rfi.Name))
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException();
                LineItem.nextItemNumber = 1;
                try // if one file fails, just report it and go on to the next 
                {
                    Meal m = LoadFromStream(sourceStream);
                    if (m is null)
                    {
                        // The stream was bad so do not store it
                        DebugMsg($"In Meal.RecoverFromRemoteAsync: LoadFromStream returned null for {rfi.Name}");
                        filesInError++;
                        if (Utilities.IsDebug)
                            Debugger.Break();
                    }
                    else if (m.Size <= 0)
                    {
                        // The stream was bad so do not store it
                        DebugMsg($"In Meal.RecoverFromRemoteAsync: LoadFromStream returned a negative size for {rfi.Name}");
                        // This could represent a networking error, in which case all subsequent files will probably fail too, so just give up
                        filesInError++;
                        if (!App.IsCloudAccessible)
                        {
                            DebugMsg($"In Meal.RecoverFromRemoteAsync: LoadFromStream detected the cloud was no longer accessible, so exit the loop");
                            break;
                        }
                        if (Utilities.IsDebug)
                            Debugger.Break();
                    }
                    else
                    {
                        if (m.Summary.FileNameInconsistent(rfi.Name))
                        {
                            // The creation time stored in the stream did not match the file name 
                            DebugMsg($"In Meal.RecoverFromRemoteAsync: LoadFromStream returned a mismatched name for {rfi.Name + ".xml"} not {m.DebugDisplay}");
                            // TODO: Move the file so it will not cause trouble in future 
                            filesInError++;
                        }
                        else // The MealSummary is good as far as we can tell
                        {
                            // this is a handy place to scan multiple bills to check for differences between the old and new DistributeCosts algorithms
                            decimal difference = m.CompareCostDistribution(report: false);
                            if (Utilities.IsDebug && (difference) > 0)
                            {
                                DebugMsg($"In Meal.RecoverFromRemoteAsync: Cost Mismatch of {difference:C} in {m.DebugDisplay}");
                                costMismatches++;
                                totalDifference += difference;
                            }
                            m.SavedToRemote = true;
                            m.Summary.IsRemote = true;
                            m.SaveToFile();
                            m.Summary.LocationChanged(isLocal: true);
                            filesWithoutError++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugMsg($"In Meal.RecoverFromRemoteAsync, exception: {ex.Message}");
                    filesInError++;
                }
                finally
                {
                    ReportProgress(totalFiles, filesWithoutError, filesInError);
                }
            }
        }
        if (costMismatches > 0)
            DebugMsg($"In Meal.RecoverFromRemoteAsync: {costMismatches} Cost Mismatches totaling {totalDifference:C}");
        if (totalFiles <= 0)
            await Utilities.DisplayAlertAsync("Download Bills", $"There were no cloud-only bills to download");
        else if (filesInError == 0)
            await Utilities.DisplayAlertAsync("Download Bills", $"All remaining cloud-only bills ({filesWithoutError}) have been downloaded without error");
        else if (filesWithoutError + filesInError < totalFiles)
            await Utilities.DisplayAlertAsync("Download Bills", $"The download was interrupted, {filesWithoutError} of {totalFiles} bills were downloaded without error");
        else
            await Utilities.DisplayAlertAsync("Download Bills", 
                $"{filesWithoutError} cloud-only bills have been downloaded, {filesInError} more had errors");
    }
    private static async Task BackupLoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            MealSummary ms = await backupQueue.DequeueAsync(cancellationToken);
            await App.CloudAllowedSource.WaitWhilePausedAsync();
            cancellationToken.ThrowIfCancellationRequested();
            Meal m = LoadFromFile(ms);
            if (m is not null) // there's a small timing hole where the file might be removed while the request is in the queue
                await m.SaveToRemoteAsync();
            else
            {
                DebugMsg($"Null meal detected in BackupLoopAsync for summary: {ms}");
                if (Utilities.IsDebug)
                    Debugger.Break();
            }
        }
    }
    /// <summary>
    /// Enqueue a meal for backup to remote storage, usually either as a result of scanning the list of meals
    /// and finding some that are local only or saving a new meal to local storage.
    /// </summary>
    /// <param name="ms"></param>
    public static void QueueForBackup(MealSummary ms) => backupQueue.Enqueue(ms);
    #endregion
}

