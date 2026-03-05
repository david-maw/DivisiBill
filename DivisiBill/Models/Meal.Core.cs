using CommunityToolkit.Mvvm.ComponentModel;
using DivisiBill.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using static DivisiBill.Services.Utilities;
using File = System.IO.File;

namespace DivisiBill.Models;

/// <summary>
/// Core state and lifecycle management for a meal (bill), including initialization, current-meal tracking,
/// lifetime rules, and high-level load/save orchestration.
/// </summary>
public partial class Meal : ObservableObjectPlus
{
    #region Global
    // Static items shared by all instances of the class
    public const string MealFolderName = "Meals";
    public const string SuspectFolderName = "Suspect";
    public const string DeletedItemFolderName = "Deleted";
    public const string ImageFolderName = "Images";
    public static readonly string MealFolderPath = Path.Combine(App.BaseFolderPath, MealFolderName);
    public static readonly string SuspectFolderPath = Path.Combine(App.BaseFolderPath, SuspectFolderName);
    internal static readonly string DeletedItemFolderPath = Path.Combine(App.BaseFolderPath, DeletedItemFolderName);
    public static readonly string ImageFolderPath = Path.Combine(App.BaseFolderPath, ImageFolderName);
    public static readonly string TempFolderPath = Path.Combine(App.BaseFolderPath, "Temp");
    public static readonly string TempImageFilePath = Path.Combine(App.BaseFolderPath, ImageFolderName, "NewImage.jpg");

    private static XmlSerializer MealSerializer { get => field ??= new XmlSerializer(typeof(Meal)); set; } = null;
    #endregion
    #region Construction
    public Meal() // public constructor needed for deserialization
    {
        // Set up required objects
        savedLineItems = [];
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
                App.Settings ??= new AppSettings();
                await GetLocalMealListAsync();
                ;
                if (LocalMealList.Count == 0 && Utilities.IsDebug)
                {
                    await StatusMsgAsync("Creating fake bill list so we have something to work with");
                    CreateFakeStoredBills();
                }
                Meal AppMeal = LoadFromApp(tryExistingSummary: true); // load the meal but use an existing summary if there is one

                if (!App.RecentlyUsed && AppMeal is not null && AppMeal.TooOldToContinue)
                {
                    // Determine which mealSummary is closest so we can use it instead of the old one
                    MealSummary closestMealSummary = null;
                    Venue closestVenue = null;
                    foreach (MealSummary ms in LocalMealList)
                    {
                        if (App.UseLocation)
                        {
                            var v = Venue.FindVenueByName(ms.VenueName);
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

                    Meal fake = new();
                    fake.LoadFakeSettings();
                    fake.Summary.CreationTime = DateTime.Now;
                    CurrentMeal = fake; // We wait to assign it until the meal id fully formed and has a venue name
                    LocalMealList.Insert(0, CurrentMeal.Summary); // ensure it is in the local meal list
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
    /// Used when App is resuming
    /// </summary>
    /// <returns></returns>
    public static async Task ResumeAsync()
    {
        await TrySaveOldBillAsync();
        if (!App.RecentlyUsed && CurrentMeal is not null && CurrentMeal.TooOldToContinue) // Maybe we need to replace the current meal with the closest one
        {
            MealSummary ClosestMealSummary = CurrentMeal.Summary;
            foreach (MealSummary ms in LocalMealList.Where(ms1 => ms1.CompareDistanceTo(ClosestMealSummary) < 0))
                ClosestMealSummary = ms;
            if (ClosestMealSummary != CurrentMeal.Summary)
            {
                Meal closestMeal = LoadFromFile(ClosestMealSummary, true);
                closestMeal?.OverwriteCurrent();
            }
        }
    }

    private static readonly PauseTokenSource SnapshotNeeded = new();

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
        Summary.HasRemoteImage = false;
        Frozen = true;
        SaveToApp(); // We do want to save it to App storage so the old version doesn't reappear after a restart
    }
    /// <summary>
    /// Call this on a meal to have it overwrite the current one and (via events) trigger actions like regenerating meal lists.
    /// Normally this is called via <see cref="BecomeCurrentMealAsync"/> but can be used directly if the current meal is to be 
    /// discarded (for example in Archive Restore).
    /// </summary>
    public void OverwriteCurrent()
    {
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
    public static Meal CurrentMeal
    {
        get;
        private set
        {
            if (field != value)
            {
                MealSummary prior = field?.Summary;
                field = value;
                Venue.SetCurrentByName(value?.VenueName);
                CurrentMealSummaryChanged?.Invoke(prior, value?.Summary);
            }
        }
    }

    public delegate void CurrentMealSummaryChangedEventHandler(MealSummary oldSummary, MealSummary newSummary);

    public static event CurrentMealSummaryChangedEventHandler CurrentMealSummaryChanged;

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
        while (true)
        {
            if (!(bool)CurrentMeal?.SavedToApp)
                CurrentMeal.SaveToApp();
            SnapshotNeeded.IsPaused = true;
            // Wait for delayTime seconds or until a request to check immediately is received
            await Task.WhenAny(Task.Delay(delayTime * 1000), SnapshotNeeded.WaitWhilePausedAsync());
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

    [GeneratedRegex(@"\d{14}\.xml")]
    private static partial Regex FourteenDigitsRegex();

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
            List<string> files = [.. Directory.EnumerateFiles(MealFolderPath, "??????????????.xml")
                                 .Select(fp => Path.GetFileName(fp))
                                 .Where(fn => FourteenDigitsRegex().IsMatch(fn)) // 14 digits dot xml (yyyymmddhhmmss.xml)
                                 .OrderByDescending(fn => fn)];
            await StatusMsgAsync($"Found {files.Count} candidate meal files");
            List<string> oldFiles = [.. LocalMealList.Select(ms => ms.FileName)]; // The order will have been determined the line above in a previous call 
            if (!Enumerable.SequenceEqual(files, oldFiles))
            {
                // The list of files has changed, so evaluate what's there now
                // First, the Meals which are now missing should be marked as not local and removed from the local list (they may still be in the remote list)
                var newFilenames = files.ToDictionary(fn => fn);
                var missingList = LocalMealList.Where(ms => !newFilenames.ContainsKey(ms.FileName)).ToList(); // a separate list because we're changing LocalMealList
                foreach (MealSummary ms in missingList)
                {
                    ms.IsLocal = false;
                    LocalMealList.Remove(ms);
                }
                // What's left has a corresponding file
                var existingLocalMs = LocalMealList.ToDictionary(ms => ms.FileName);
                var existingRemoteMs = RemoteMealList.ToDictionary(ms => ms.FileName);
                // Iterate through the stored meals and create a  MealSummary object for each
                // The list of MealSummary objects is what is stored in LocalMealList, and it includes the presence of an image file if one exists
                foreach (string fileName in files.Where(fn => !existingLocalMs.ContainsKey(fn)))
                {
                    if (existingRemoteMs.TryGetValue(fileName, out MealSummary ms))
                    {
                        // The MealSummary is already in the RemoteMealList, so just mark it as local too and add it to LocalMealList
                        // because local meals are automatically backed up, this is the most common case
                        ms.IsLocal = true;
                    }
                    else
                    {
                        // This is a brand new Meal, not previously seen
                        ms = null;
                        Task<MealSummary> T = new(() => MealSummary.LoadFromMealFile(fileName));
                        T.Start();
                        try
                        {
                            await T;
                            ms = T.Result;
                        }
                        catch (Exception ex)
                        {
                            FileStream fileStream = File.Create(Path.Combine(Meal.MealFolderPath, fileName));
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
            LocalMealList.Clear();
        await StatusMsgAsync("Established local meal list");
    }

    /// <summary>
    /// Add new meals into the existing LocalMealList and store each one locally, used for archive restore
    /// </summary>
    /// <param name="newMeals">An enumerable list of Meal items</param>
    public static async Task AddLocalMeals(IEnumerable<Meal> newMeals, bool replace)
    {
        bool wasEmpty = LocalMealList.Count == 0;
        HashSet<string> localMealNames = [.. LocalMealList.Where(ms => !ms.IsDefault).Select(ms => ms.Id)];
        foreach (Meal meal in newMeals)
        {
            if (meal.Size < 0)
                continue; // This is a bad bill
            // Remove the old version of the meal if it is being replaced
            if (replace && localMealNames.Contains(meal.Summary.Id))
            {
                LocalMealList.Remove(meal.Summary);
                localMealNames.Remove(meal.Summary.Id);
            }
            if (!localMealNames.Contains(meal.Summary.Id))
            {
                if (meal.IsLastChangeTimeSet || !File.Exists(meal.FilePath)) // never replace an unchanged meal
                {
                    meal.SaveToFile();
                    meal.Summary.IsLocal = true;
                }
                // If the original list was empty, the new item may be added, otherwise it must be inserted in the correct place
                if (wasEmpty)
                    LocalMealList.Add(meal.Summary);
                else
                    LocalMealList.Upsert(meal.Summary);
                localMealNames.Add(meal.Summary.Id); // to ensure we do not add duplicates
            }
        }
        // Get the list of remote meals if we can (and should) and update whatever local meals are also remote
        if (App.IsCloudAllowed)
        {
            await App.CloudAllowedSource.WaitWhilePausedAsync();
            Utilities.DebugMsg("In BackupToRemoteAsync, CloudAllowedSource no longer paused");
            // This is where all the elapsed time goes, reaching out over the network, so we don't wait for it
            _ = BackupMissingAsync();
        }
    }
    /// <summary>
    /// Select all but the latest meal for each venue from local storage, note that after calling this the MealListViewModel.SelectedMealSummariesCount will be wrong
    /// </summary>
    internal static bool SelectOlder()
    {
        IOrderedEnumerable<MealSummary> list = LocalMealList.Where(ms => ms.IsLocal).OrderBy(ms => ms.VenueName).ThenByDescending(ms => ms.CreationTime);
        int distinctCount = list.DistinctBy(ms => ms.VenueName).Count();
        if (list.Count() == distinctCount)
            return false; // nothing to do
        string priorVenue = string.Empty;
        foreach (MealSummary ms in list)
            if (!(ms.FileSelected = priorVenue.Equals(ms.VenueName)))
                priorVenue = ms.VenueName;
        return true;
    }

    /// <summary>
    /// List of locally resident meals (though it is actually a list of meal summaries each representing a meal) in reverse order of creation
    /// time (so, newest first). Where a Meal is present both locally and remotely a reference to the same MealSummary is in both this and the remote list. 
    /// </summary>
    public static ObservableCollection<MealSummary> LocalMealList { get; } = [];

    /// <summary>
    /// List of cloud resident meals (though it is actually a list of meal summaries each representing a meal) in reverse order of creation
    /// time (so, newest first). Where a Meal is present both locally and remotely a reference to the same MealSummary is in both this and the local list. 
    /// </summary>
    public static ObservableCollection<MealSummary> RemoteMealList { get; } = [];
    #endregion
    #region Shared
    public override string ToString() => ToString(null);

    /// <summary>
    /// Returns a string representation of the cost information for the specified person or everyone
    /// by calling <see cref="TextToStream"/> and reading the result back into a string.
    /// </summary>
    /// <param name="whoFor">The person for whom the cost information is to be formatted, null if it 
    /// is not for a specific participant.</param>
    /// <returns>A string containing the formatted cost details.</returns>
    public string ToString(PersonCost whoFor)
    {
        MemoryStream ms = new();
        TextToStream(ms, whoFor);
        ms.Position = 0;
        StreamReader reader = new(ms);

        string text = reader.ReadToEnd();
        return text;
    }

    /// <summary>
    /// Writes a formatted textual representation of the bill, including participants, items, and totals, to the
    /// specified stream.
    /// </summary>
    /// <remarks>The output includes bill metadata, participant amounts, itemized charges, and totals. If an
    /// exception occurs during writing, the exception message is appended to the output. The method does not close the
    /// provided stream.</remarks>
    /// <param name="stream">The stream to which the bill text will be written. Must be writable and remain open for the duration of the
    /// operation.</param>
    /// <param name="whoFor">An optional person for whom to calculate and display individual share information. If null, the output includes
    /// only overall bill details.</param>
    private void TextToStream(Stream stream, PersonCost whoFor = null)
    {
        StreamWriter sw = new(stream);
        try
        {
            sw.WriteLine("DivisiBill " + Utilities.VersionName + "." + Utilities.Revision);
            #region Bill Properties
            if ((whoFor is not null) && (whoFor.Diner is not null))
                sw.WriteLine("Calculation for {0}", whoFor.Diner.DisplayName);
            sw.WriteLine("Venue " + VenueName);
            sw.WriteLine("Created {0:F}", CreationTime);
            if (IsLastChangeTimeSet && (LastChangeTime - CreationTime).Duration() > TimeSpan.FromSeconds(1))
                sw.WriteLine($"Updated {LastChangeTime:F}");
            sw.WriteLine("Tax Rate {0:P2}    Tip Rate {1:P0}\r\n", TaxRate, TipRate);
            #endregion
            #region Participant List and Amounts
            int maxDinerIndex = 0;
            foreach (PersonCost pc in Costs)
            {
                if (pc.Amount != 0)
                    sw.WriteLine("{0} {1, -40} {2,10:C}", (byte)pc.DinerID % 10,
                       pc.Diner is null ? pc.Nickname : pc.Diner.DisplayName,
                       pc.Amount);
                maxDinerIndex = Math.Max(maxDinerIndex, pc.DinerIndex % 10);
            }
            if (IsAnyUnallocated)
                sw.WriteLine("{0, -42} {1,10:C}", "Unallocated", UnallocatedAmount);
            #endregion
            #region Item List
            sw.WriteLine();
            sw.Write("{0,10}  {1, -30} {2,10}", "Sharers", "Item", "Amount"); // Heading
            if (whoFor is not null)
                sw.WriteLine(" {0,10}", "Share"); // Per person shares
            else
                sw.WriteLine();
            decimal dinerSubTotal = 0;
            string spaces = new(' ', LineItem.maxSharers - 1 - maxDinerIndex);
            foreach (LineItem lineItem in LineItems)
            {
                sw.Write(spaces);
                for (int i = maxDinerIndex; i >= 0; i--)
                {
                    if (lineItem.SharedBy[i])
                        sw.Write((i + 1) % 10);
                    else
                        sw.Write(".");
                }
                string lineItemText = lineItem.ItemName + (lineItem.Comped ? " (comped)" : "");
                sw.Write($"  {lineItemText,-30} {lineItem.Amount,10:C}"); // add an extra space to make negative numbers line up 
                if (whoFor is not null)
                {
                    decimal dinerAmount = lineItem.GetAmounts()[(int)whoFor.DinerID - 1];
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
            if (GetCouponAmountIfBeforeTax() != 0)
                sw.WriteLine("            {0, -30} {1,10:C}", "Coupons", GetCouponAmountIfBeforeTax());
            sw.Write("            {0, -30} {1,10:C}", "Subtotal", SubTotal);
            if (dinerSubTotal != 0)
                sw.Write(" {0,10:C}", dinerSubTotal);
            sw.WriteLine();
            sw.WriteLine();
            sw.WriteLine("            {0, -30} {1,10:C}", "Tax", Tax);
            if (CouponAmountIfAfterTax != 0)
                sw.WriteLine("            {0, -30} {1,10:C}", "Discount After Tax", -CouponAmountIfAfterTax);
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

    /// <summary>
    /// Creates and sends an email message containing the bill details to one or more recipients asynchronously.
    /// </summary>
    /// <remarks>The email includes the bill details in the message body and attaches both an archive and a
    /// text file copy of the bill. If email functionality is not supported on the device, the operation will fail
    /// silently after reporting the error.</remarks>
    /// <param name="whoFor">The person for whom the bill should be sent. If null, the bill is sent to all diners with a valid email address.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task CreateEmailMessageAsync(PersonCost whoFor = null)
    {
        List<string> recipients = [];
        if (whoFor is null) // send it to everyone
        {
            foreach (PersonCost pc in Costs.Where(pc => !string.IsNullOrWhiteSpace(pc.Diner?.Email)))
                recipients.Add(pc.Diner.Email);
        }
        else // send it to just the one person
        {
            if (!string.IsNullOrWhiteSpace(whoFor.Diner?.Email))
                recipients.Add(whoFor.Diner.Email);
        }
        string body = ToString(whoFor);
        EmailMessage message = new()
        {
            Subject = "DivisiBill sent you a bill",
            Body = body,
            To = recipients
        };
        if (!string.IsNullOrEmpty(VenueName))
            message.Subject += " from " + VenueName;

        // Make an archive and attach it
        string zipFullName = CreateZipArchive();
        // Attach archive file
        message.Attachments.Add(new EmailAttachment(zipFullName));
        // Attach a copy of the message in a text file to make it easier to read.
        string fn = "Bill-" + CreationTime.ToString("yyyyMMddHHmmss") + ".txt";
        string tempFileFullName = Path.Combine(TempFolderPath, fn);
        File.WriteAllText(tempFileFullName, body);
        message.Attachments.Add(new EmailAttachment(tempFileFullName));
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
        // Delete the temporary files used for attachments but give the email system time to read them first (Android fails without this)
        await Task.Run(async () =>
        {
            try
            {
                await Task.Delay(60000);
                File.Delete(tempFileFullName);
                if (!string.IsNullOrWhiteSpace(zipFullName))
                    File.Delete(zipFullName);
            }
            catch (Exception)
            {
                // Simply ignore it
                Utilities.DebugMsg("Exception deleting temporary files for mail ignored");
            }
        });
    }
    private void SetupChangedEvents()
    {
        foreach (LineItem item in LineItems)
            item.PropertyChanged += OnLineItemChange;
        LineItems.CollectionChanged += LineItems_CollectionChanged; // Will take care of any future additions and deletions from LineItems
        Costs.CollectionChanged += Costs_CollectionChanged;
        Summary.PropertyChanged += Summary_PropertyChanged;
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
    #endregion
    #region Creating Fakes
    /// <summary>
    /// Creates a fake bill
    /// </summary>
    /// <param name="ms">The MealSummary to associate with this fake meal</param>
    /// <returns></returns>
    public static Meal LoadFake(MealSummary ms)
    {
        Meal m = new() { Summary = ms };
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
        CreateFakeCosts();
        CreateFakeLineItems();
        TaxRate = 0.0775;
        TipRate = 0.20;
        Frozen = true;
        FinalizeSetup();
    }

    public static void CreateFakeStoredBills()
    {
        string venueName1 = Venue.AllVenues.FirstOrDefault()?.Name ?? "Queasy Diner";
        string venueName2 = Venue.AllVenues.Skip(1).FirstOrDefault()?.Name ?? "Bad Pizza";
        // Note that they are added in order
        LocalMealList.Add(new MealSummary()
        {
            VenueName = venueName1,
            CreationTime = new DateTime(2021, 1, 2, 3, 4, 5),
        });
        LocalMealList.Add(new MealSummary()
        {
            VenueName = venueName2,
            CreationTime = new DateTime(2010, 11, 12, 14, 43, 20),
            FileSelected = true
        });
        LocalMealList.Add(new MealSummary()
        {
            VenueName = venueName1,
            CreationTime = new DateTime(2010, 11, 11, 11, 11, 11),
        });
    }

    private void CreateFakeLineItems() => LineItems =
        [
            new LineItem(){Amount =  20, SharesList = "111", ItemName = "Appetizer" },
            new LineItem(){Amount = -10, SharesList = "111", ItemName = "Discount" },
            new LineItem(){Amount =  30, SharesList = "001", ItemName = "Tasty Chicken" },
            new LineItem(){Amount =  40, SharesList = "100", ItemName = "Overdone Beef", Comped = true },
            new LineItem(){Amount =  60, SharesList = "210", ItemName = "Wine" },
            new LineItem(){Amount =  20, SharesList = "010", ItemName = "Fish & Chips" },
            new LineItem(){Amount =   5,                     ItemName = "Mystery item" },
        ];

    private void CreateFakeCosts()
    {
        Costs = [];
        for (int i = 0; i < 3; i++)
        {
            PersonCost pc = new() { DinerID = (LineItem.DinerID)(i + 1), Diner = Person.AllPeople[i] };
            Costs.Add(pc);
        }
    }
    #endregion
    #region Miscellaneous
    private bool MonitorChanges;

    public string ApproximateAge => CreationTime.ApproximateAge();
    private TimeSpan Age => DateTime.Now - CreationTime;
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
        List<Person> newPeople = [];
        int nextNumber = 1;
        foreach (PersonCost personCost in Costs.Where(pc => pc.Diner is null)) // Rare case where the guid didn't correspond to a known person
        {
            if (Costs.Count(pc => pc.Nickname.Equals(personCost.Nickname)) > 1) // The same nickname is repeated, so make this one unique
            {
                personCost.Nickname += nextNumber; // Note the +=
                nextNumber++;
            }
            Person p = new(personCost.PersonGUID)
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
    // Create a crash report - this will be sent immediately (it doesn't wait for a program restart)
    public static void ReportCrash(string What, string Who, Stream sourceStream, Exception ex, string streamName, string errorDescription = "")
    {
        string errorMessage = $"Meal.ReportCrash reported What={What}, Who={Who}, Exception={ex}";
        Debug.WriteLine(errorMessage);

        if (!string.IsNullOrEmpty(errorDescription))
            errorMessage += "\n" + errorDescription + "\n";

        ex.ReportCrash(errorMessage, sourceStream, streamName);
    }

    [XmlIgnore]
    public MealSummary Summary
    {
        private set
        {
            if (value != field)
            {
                if (field is not null)
                {
                    field.PropertyChanged -= Summary_PropertyChanged;
                    if (!string.IsNullOrEmpty(value?.VenueName) && !string.IsNullOrEmpty(field.VenueName))
                    {
                        Debug.Assert(value.VenueName == field.VenueName
                            && Utilities.WithinOneSecond(value.CreationTime, field.CreationTime),
                            "A Summary replacement would change significant properties");
                    }
                }
                field = value;
                field.PropertyChanged += Summary_PropertyChanged;
                MarkAsChanged();
            }
        }
        get => field ??= new MealSummary();
    }

    /// <summary>
    /// Forwards property change notifications from the MealSummary to the Meal
    /// </summary>
    private void Summary_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
        if (e.PropertyName is (nameof(MealSummary.IsLocal)) or (nameof(MealSummary.IsRemote)))
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
        }
        ;
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
    #endregion
    #region Image management
    /// <summary>
    /// The fully qualified path to the bill image for this bill
    /// </summary>
    public string ImagePath => Summary.ImagePath;
    public bool HasImage => Summary.HasImage;
    public bool HasDeletedImage => Summary.HasDeletedImage;
    public bool HasRemoteImage => Summary.HasRemoteImage;
    public void DeleteImage() => Summary.DeleteImage();
    public void TryUndeleteImage() => Summary.TryUndeleteImage();
    public bool ReplaceImage(string s) => Summary.ReplaceImage(s);

    /// <summary>
    /// Permanently deletes all local image files from storage, removing them without the possibility of recovery.
    /// </summary>
    /// <remarks>This method does not delete any associated meals. Use this method when a complete removal of local
    /// image data is required, such as during an archive restore operation.</remarks>
    public static void PermanentlyDeleteAllLocalImages()
    {
        var imageFiles = Directory.EnumerateFiles(ImageFolderPath, "??????????????.jpg").ToList();

        foreach (string imageFilePath in imageFiles)
        {
            try
            {
                File.Delete(imageFilePath);
            }
            catch (Exception ex)
            {
                ex.ReportCrash();
            }
        }
    }
    #endregion
    #region Change Monitoring
    /// <summary>
    /// Marks the current bill as changed, updating its state and associated summaries as needed.
    /// </summary>
    /// <remarks>If the bill is frozen, this method creates a new bill instance with updated timestamps and
    /// summary information, and notifies related components of the change. Subsequent calls will update the last change
    /// time. After calling this method, the bill is considered modified and will require saving to persist
    /// changes.</remarks>
    public void MarkAsChanged()
    {
        if (!MonitorChanges)
            return;
        if (Frozen)
        {   // We're going to make an identical new bill from the same venue except the CreationTime will be 'now'
            // We know there's already a persisted copy of the current bill (that's what 'Frozen' means). 
            // However, the current bill MealSummary will be in the summary list, so stop using it and make a new one
            // and notify the MealSummary class that we did that, so the old summary is no longer current, the new one is
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
                // difficult by the fact that the CreationTime has been changed, but we can use the original FileName
                if (string.IsNullOrEmpty(FileName))
                {
                    if (Utilities.IsDebug)
                        Debugger.Break();
                }
                else
                {
                    try
                    {
                        File.Copy(OriginalSummary.ImagePath, Summary.ImagePath);
                    }
                    catch (Exception)
                    {
                        if (Debugger.IsAttached)
                            Debugger.Break();
                        // Not the end of the world if this fails, so just go on without it
                        Summary.DeleteImage();
                    }
                    finally
                    {
                        Summary.CheckImageFiles();
                    }
                }
            }
            Summary.IsRemote = false; // In due course this will be picked up by backupLoopAsync 
            Summary.HasRemoteImage = false; // because it is a new bill, with a new name
            SaveToFile();
            Summary.Show();
            CurrentMealSummaryChanged?.Invoke(OriginalSummary, Summary);
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
    private void OnLineItemChange(object sender, PropertyChangedEventArgs e)
    {
        MarkAsChanged();
        if (e.PropertyName.Equals(nameof(LineItem.Amount)) || e.PropertyName.Equals(nameof(LineItem.Comped)))
            UpdateAmounts();
        else if (e.PropertyName.Equals(nameof(LineItem.SharedBy)))
            IsDistributed = false;
    }

    private void LineItems_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is System.Collections.Specialized.NotifyCollectionChangedAction.Add or
            System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
        { // Make sure the new items report any changes
            foreach (object item in e.NewItems)
                ((LineItem)item).PropertyChanged += OnLineItemChange;
        }
        if (e.Action is System.Collections.Specialized.NotifyCollectionChangedAction.Remove or
            System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
        { // Make sure the old items no longer report any changes
            foreach (object item in e.OldItems)
                ((LineItem)item).PropertyChanged -= OnLineItemChange;
            if (LineItems.Count == 0)
                LineItem.NextItemNumber = 1;
        }
        MarkAsChanged();
        UpdateAmounts();
    }

    private void Costs_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => MarkAsChanged();
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
            Venue.SetCurrentByName(value);
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
                OnPropertyChanged();
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
    /// The curious layout of the xxxTime and Actual...Time properties is because we want to store the times accurately
    /// with time zone information but show them to the human as if they were all local times, so dinner in Mumbai and Dinner 
    /// in California both show as happening in the evening. Most people only ever operate in a single time zone, but for those
    /// that do not this seems like the least bad choice. More importantly, it means that the file name and the creation time
    /// align regardless of time zone.
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
                    Summary.HasRemoteImage = false; // the image is not remote until we save it to remote storage
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
        get => ActualLastChangeTime.DateTime == DateTime.MinValue
                ? null
                : ActualLastChangeTime.ToString("s", System.Globalization.CultureInfo.InvariantCulture) + ActualLastChangeTime.ToString("zzz", System.Globalization.CultureInfo.InvariantCulture);

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

    #endregion
    #region public interface
    [XmlIgnore]
    public string DebugDisplay => "\"" + VenueName + "\"" + (IsDefault ? ", IsDefault" : $" at {CreationTime} {ApproximateAge} in {FileName}");
    public string DiagnosticInfo
    {
        get
        {
            StringBuilder info = new(Frozen ? "Frozen" : "Thawed", 100);
            if (Summary.IsLocal)
                info.Append(", IsLocal");
            if (SavedToFile)
                info.Append(", SavedToFile");
            if (Summary.IsRemote)
                info.Append(", IsRemote");
            if (SavedToRemote)
                info.Append(", SavedToRemote");
            if (HasImage)
                info.Append(", HasImage");
            if (HasRemoteImage)
                info.Append(", HasRemoteImage");
            if (HasDeletedImage)
                info.Append(", HasDeletedImage");
            return info.ToString();
        }
    }
    /// <summary>
    /// Final values have been settled on for this bill, so any future attempt to change it is actually a 
    /// new bill (possibly for the same location) and should be given a new creation time. Any bill loaded from
    /// persistent storage starts out frozen
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiagnosticInfo))]
    public partial bool Frozen { get; set; }
    #endregion
    #endregion
}