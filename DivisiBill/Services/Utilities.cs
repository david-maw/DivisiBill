using CommunityToolkit.Diagnostics;
using CommunityToolkit.Maui.Views;
using DivisiBill.Generated;
using DivisiBill.Models;
using DivisiBill.ViewModels;
using Sentry;
using Sentry.Extensibility;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace DivisiBill.Services;

/// <summary>
/// A general repository for handy capabilities used throughout the app
/// </summary>
public static class Utilities
{
#if DEBUG
    public static readonly bool IsDebug = true; // Not a const so as to avoid "unreachable code" warnings
#else
    public static readonly bool IsDebug = false;
#endif
    /// <summary>
    /// Create a pseudo random string of letters and digits for use ans a randomized key
    /// </summary>
    /// <param name="size">Length of string to create</param>
    /// <returns>Pseudo-random string of characters</returns>
    internal static string GenerateToken(int size = 50)
    {
        Guard.IsLessThan(size, 1000);
        StringBuilder randomString = new StringBuilder();

        Random random = new Random();

        // String that contain both alphabets and numbers
        string digits = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        for (int i = 0; i < size; i++)
        {

            // Selecting a index randomly
            int x = random.Next(digits.Length);

            // Appending the character at the 
            // index to the random alphanumeric string.
            randomString.Append(digits[x]);
        }
        return randomString.ToString();
    }
    /// <summary>
    /// Insert an item in an ordered list of items or move it if it is already there but should be in a different
    /// place in the list. 
    /// The list is ordered based on a compare function passed as a parameter. 
    /// This would be a lot easier if it were not for the 'move' case where the item is already in the list but in
    /// the wrong place so the list may initially have one element out of order.
    /// </summary>
    /// <typeparam name="T">The object type we're working with</typeparam>
    /// <param name="list">The list on which we are operating</param>
    /// <param name="targetItem">The item to insert or move</param>
    /// <param name="compareTo">The comparison function to determine where the item should be</param>
    /// <returns>True if a new item was inserted false if not (one was already there or the list was null)</returns>
    public static bool Upsert<T>(this IList<T> list, T targetItem, Func<T, T, int> compareTo) where T : class
    {
        // First, handle the trivial cases
        if (list is null) // initializing
            return false;
        else if (list.Count == 0)
        {
            list.Add(targetItem);
            return true;
        }
        // Go through the list to see if the item is already in it and where it should be now
        // A linear search because we do not know the former item location, if any.
        int oldIndex = -1, index = -1, newIndex = -1;
        foreach (var item in list)
        {
            index++;
            if (oldIndex < 0 && item == targetItem) // The item is already in the list
            {
                oldIndex = index;
                if (newIndex >= 0)
                    break;
            }
            else if (newIndex < 0 && compareTo(targetItem, item) <= 0) // the else clause here ensures we don't compare with the target item
            {
                newIndex = index;
                if (oldIndex >= 0)
                    break;
            }
        }
        // OldIndex == -1 means we did not find it, newIndex == -1 means we never found the correct point to insert it 
        // Now we have located the old location of the item (if any) and where we should insert it
        if (newIndex < 0)
        {
            // either it's already in the right place or it should be at the end
            if (compareTo(targetItem, list.Last()) > 0)
            { // Needs to be at the end
                if (oldIndex >= 0) // item was already in the list so remove it
                    list.RemoveAt(oldIndex);
                list.Add(targetItem);
            }
        }
        else if (oldIndex < 0) // item is not in list, so we will simply add it
            list.Insert(newIndex, targetItem);
        else
        {
            // The item was already somewhere in the existing list (including at the beginning) so we will move it
            if (newIndex - 1 >= oldIndex)
                newIndex--; // because removing the item from the old index will decrement all subsequent indexes
            if (newIndex != oldIndex)
            {
                if (list is ObservableCollection<T> coll)
                    coll.Move(oldIndex, newIndex);
                else
                {
                    list.RemoveAt(oldIndex);
                    list.Insert(newIndex, targetItem);
                }
            }
        }
        // Validation code to ensure order is correct when we exit this function
        if (IsDebug)                            
        {
            bool noErrors = true;
            T priorVenue = list.First();
            foreach (T currentVenue in list)
            {
                if (noErrors && compareTo(currentVenue, priorVenue) < 0)
                {
                    noErrors = false;
                    Debugger.Break();
                    int i = compareTo(currentVenue, priorVenue); // so the debugger can step in 
                }
                priorVenue = currentVenue;
            }
        }
        // Return false if it was already there, true if it was not
        return oldIndex < 0;
    }

    /// <summary>
    /// Special case of Upsert for IComparable items
    /// </summary>
    /// <typeparam name="T">The item type</typeparam>
    /// <param name="list"></param>
    /// <param name="targetItem"></param>
    public static bool Upsert<T>(this IList<T> list, T targetItem) where T : class, IComparable<T>
    {
        static int compareTo(T item1, T item2)
        {
            return item1.CompareTo(item2);
        }
        return Upsert(list, targetItem, compareTo);
    }

    /// <summary>
    /// Insert an item in a list before a specified item or at the end if the specified item is null
    /// </summary>
    /// <typeparam name="T">The item type</typeparam>
    /// <param name="list"></param>
    /// <param name="existingItem"></param>
    /// <param name="itemToInsert"></param>
    public static void InsertBefore<T>(this IList<T> list, T existingItem, T itemToInsert) where T : class
    {
        ArgumentNullException.ThrowIfNull(itemToInsert);
        if (existingItem is null)
            list.Add(itemToInsert);
        else
        {
            int i = list.IndexOf(existingItem);
            if (i < 0)
                throw new ArgumentOutOfRangeException(); // existingItem was not in the list
            else
                list.Insert(i, itemToInsert);
        }
    }

    /// <summary>
    /// Return the next item after the selected one is deleted (either the next or, if the last one is deleted, the previous one)
    /// </summary>
    /// <typeparam name="T">The type of items in the list (normally inferred from the parameters) </typeparam>
    /// <param name="collection">The list it is in</param>
    /// <param name="selected">The item that is currently selected</param>
    /// <returns>
    /// Normally the item after the selected one, the item before if it is the last, null if it's the only one.
    /// Returns the last item (or null if the list is empty) if the selected item is not in the list
    /// Returns the first item (or null if the list is empty) if the selected item is null.
    /// </returns>
    public static T Alternate<T>(this IReadOnlyCollection<T> collection, T selected) where T : class
    {
        bool found = false;
        T alt = default;

        if (selected is null)
            return collection.FirstOrDefault();

        foreach (var item in collection)
        {
            if (item.Equals(selected))
                found = true; // This should always happen exactly once
            else
            {
                alt = item;
                if (found)
                    break; // We found an item immediately following selected
            }
        }
        return alt;
    }

    /// <summary>
    /// Find an item and its index in an IEnumerable, usually it's easiest if this is a list.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="items"></param>
    /// <param name="predicate"></param>
    /// <returns></returns>
    public static (T, int) FindItemAndIndex<T>(this IEnumerable<T> items, Predicate<T> predicate)
    {
        int index = 0;
        foreach (var item in items)
        {
            if (predicate(item))
                return (item, index);
            index++;
        }
        return (default(T), -1);
    }

    /// <summary>
    /// Return a block of text describing the current app build. Used for diagnostic messages.
    /// </summary>
    /// <returns>text describing the current app build.</returns>
    public static string GetAppInformation()
    {
        StringBuilder s = new StringBuilder("DivisiBill ", 1000);
        s.AppendLine((App.IsLimited ? "Basic" : "Professional") + " Edition");
        s.AppendLine("Version " + VersionName + " build " + Revision + " at " + BuildTime);
        if (Billing.ProPurchase is not null)
        {
            s.AppendLine("Professional Edition Purchase ID: " + Billing.ProPurchase.Id
                    + ", PurchaseState = " + Billing.ProPurchase.State);
            if (!string.IsNullOrEmpty(App.Settings?.UserKey))
                s.AppendLine("Professional Edition Storage Key: " + App.Settings.UserKey);
        }
        if (Billing.OcrPurchase is not null)
            s.AppendLine("OCR Purchase ID: " + Billing.OcrPurchase.Id + ", scans remaining = " + Billing.ScansLeft
                + ", PurchaseState = " + Billing.OcrPurchase.State);
        return s.ToString();
    }

    /// <summary>
    /// Report an exception with optional explanatory text
    /// </summary>
    /// <param name="comment"></param>
    /// <param name="ex"></param>
    /// <param name="sourceStream"></param>
    /// <param name="streamName"></param>
    /// <param name="callerFilePath"></param>
    /// <param name="methodName"></param>
    /// <param name="callerLineNumber"></param>
    public static void ReportCrash(this Exception ex, string comment = "", Stream sourceStream = null, string streamName = null, [CallerFilePath] string callerFilePath = null, [CallerMemberName] string methodName = null, [CallerLineNumber] int callerLineNumber = 0)
    {
        string errText = $"Utilities.ReportCrash called from {methodName} ({callerFilePath} {callerLineNumber})";
        DebugMsg(errText + ", ex:" + ex);

        StringBuilder crashMsg = new(errText);

        crashMsg.AppendLine($"\nUser key = {App.Settings?.UserKey}");
        if (string.IsNullOrEmpty(comment))
            crashMsg.AppendLine("No comment");
        else
            crashMsg.AppendLine("Comment:\n" + comment + "\n");

        if (sourceStream is null && Meal.CurrentMeal?.Summary?.SnapshotStream is not null)
        {
            sourceStream = Meal.CurrentMeal.Summary.SnapshotStream;
            streamName = Meal.CurrentMeal.FileName;
        }
        if (sourceStream is not null && !(sourceStream.CanRead && sourceStream.Length > 0 && sourceStream.CanSeek))
            sourceStream = null;
        SentrySdk.CaptureException(ex, scope => {
            if (ex is XmlException xmlEx)
                scope.Fingerprint = new[] { "xml-error" }; // this seems to be ignored
            // These attachments appear in the Sentry UI in reverse of the order they appear here
            if (Meal.CurrentMeal?.Summary is not null)
            {
                if (File.Exists(Meal.CurrentMeal.FilePath))
                {
                    scope.AddAttachment(Meal.CurrentMeal.FilePath);
                    crashMsg.AppendLine("Attaching meal file: " + Meal.CurrentMeal.FileName);
                }
                // Attach a copy of the bill image if there is one
                if (Meal.CurrentMeal.HasImage && File.Exists(Meal.CurrentMeal.ImagePath))
                {
                    scope.AddAttachment(Meal.CurrentMeal.ImagePath);
                    crashMsg.AppendLine("Attaching image file: " + Meal.CurrentMeal.ImageName);
                }
            }
            // Attach the reported stream
            if (sourceStream is not null && sourceStream.CanSeek) 
            {
                sourceStream.Position = 0;
                streamName = "stream-" + (string.IsNullOrWhiteSpace(streamName) ? "anonymous.txt" : streamName);
                string streamPath = Path.Combine(FileSystem.Current.CacheDirectory, streamName);
                using (var fileStream = File.Create(streamPath))
                    sourceStream.CopyTo(fileStream);
                scope.AddAttachment(streamPath);
                File.Delete(streamPath);
                crashMsg.AppendLine("Attaching data stream content: " + streamName);
            }
            scope.AddAttachment(Encoding.Latin1.GetBytes(crashMsg.ToString()), "Comment.txt", AttachmentType.Default, "text/plain");
        });
    }
    /// <summary>
    /// Diagnostic messages visible in release builds, not just debug builds
    /// </summary>
    /// <param name="msg"></param>
    /// <param name="sourceFilePath"></param>
    /// <param name="sourceLineNumber"></param>
    public static void RecordMsg(string msg,
        [CallerMemberName] string callerName = "unknown",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        string fullMsg = sourceFilePath.Substring(sourceFilePath.LastIndexOf(@"\") + 1) + "@" + sourceLineNumber + " (" + callerName  + "): " + msg;
        SentrySdk.AddBreadcrumb(
            type: "debug",
            category: "Record." + callerName,
            message: fullMsg);
        DebugMsg(fullMsg);
    }

    /// <summary>
    /// Send a status message to subscribers of StatusMsgInvoked (used to report progress during initialization)
    /// </summary>
    public delegate void SendMsg(string msg);

    public static event SendMsg StatusMsgInvoked;

    private static readonly PauseTokenSource PauseBeforeMessageSource = new PauseTokenSource();
    private static bool pauseBeforeMessage;
    public static bool PauseBeforeMessage
    {
        get => pauseBeforeMessage;
        set
        {
            if (pauseBeforeMessage != value)
            {
                pauseBeforeMessage = value;
                PauseBeforeMessageSource.IsPaused = value;
            }
        }
    }

    /// <summary>
    /// Send a status message to subscribers of StatusMsgInvoked (used to report progress during initialization).
    /// These messages are also put in the breadcrumb trail for context in case there's an unexpected failure.
    /// </summary>
    public async static Task StatusMsgAsync(string msg, 
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        await PauseBeforeMessageSource.WaitWhilePausedAsync();
        StatusMsgInvoked?.Invoke(msg);
        // Extract just the file name - has to be done manually because this may be an Android build compiled on Windows
        var sourceFileName = sourceFilePath.Substring(sourceFilePath.LastIndexOf(@"\") + 1);
        SentrySdk.AddBreadcrumb(
            type: "debug", 
            category: "Utilities.StatusMsg",
            message: sourceFileName + "@" + sourceLineNumber + ": " + msg);
        DebugMsg(msg);
    }

    /// <summary>
    /// See if Contacts permission is available, the permission is not required. 
    /// </summary>
    /// <returns>Whether the permission has been granted</returns>

    public static async Task<bool> HasContactsReadPermissionAsync() => await CheckAndRequestPermissionAsync(new Permissions.ContactsRead(),
            () => DisplayAlertAsync("Contacts Access", "Select \"Allow\" on the next screen to permit DivisiBill to select a contact from your contact list"));

    /// <summary>
    /// See if Location permission is available, it is not required. 
    /// </summary>
    /// <returns>Whether the permission has been granted</returns>
    public static async Task<bool> HasLocationPermissionAsync() => await CheckAndRequestPermissionAsync(new Permissions.LocationWhenInUse(),
            () => DisplayAlertAsync("Location Access", "Allow DivisiBill access to location on the next dialog if you want to store and retrieve bills by location"));

    /// <summary>
    /// See if a permission is available, ask for it if not, show a rationale for it if we are told to.
    /// The basic flow is to ask for the permission, if it is granted use it, if not then either just give
    /// up if we've already shown a rationale, or should not show one. After denying the request once Android will 
    /// offer the user a "Don't ask again" option and if that is chosen requests will be automatically denied in future
    /// with no rational displayed, that feature will presumably simply be unused. The exact behavior of Android depends on
    /// the release level and much of the "do not ask again" logic is not needed in later (32+) versions.
    /// </summary>
    /// <param name="Explain">A function to call to explain to the user why the permission is needed</param>
    /// <returns>a boolean indicating whether the requested permission was granted</returns>
    public static async Task<bool> CheckAndRequestPermissionAsync<T>(T permission, Func<Task> Explain) where T : Permissions.BasePermission
    {
        try
        {
            var status = await permission.CheckStatusAsync();
            if (status == PermissionStatus.Granted)
                return true;
            bool asked = permission.ShouldShowRationale();
            if (asked)
                await Explain();
            status = await permission.RequestAsync();
            if (status == PermissionStatus.Granted)
                return true;
            if (asked || !permission.ShouldShowRationale())
                return false;
            await Explain();
            status = await permission.RequestAsync();
            return status == PermissionStatus.Granted;
        }
        catch (Exception ex)
        {
            ReportCrash(ex);
            return false;
        }
    }

    /// <summary>
    /// True if we're running on Windows
    /// </summary>
    public static bool IsUWP => DeviceInfo.Platform == DevicePlatform.WinUI;
    /// <summary>
    /// True if we're running on Android
    /// </summary>
    public static bool IsAndroid => DeviceInfo.Platform == DevicePlatform.Android;

    // Stops the count of milliseconds before the first message getting silly.
    static DateTime startTime = DateTime.Now;
    // A timer for use with diagnostic messages
    static double lastSeconds = 0;

    [Conditional("DEBUG")]
    public static void DebugExamineStream(Stream streamParameter)
    {
        if (Debugger.IsAttached)
        {
            //Testing - normally used to allow stored XML to be examined in myString
            long savedPosition = streamParameter.Position;
            streamParameter.Position = 0;
            StreamReader sr = new(streamParameter);
            string myString = sr.ReadToEnd();
            streamParameter.Position = savedPosition;
        }
    }
    /// <summary>
    /// Standardized format diagnostic messages for the programmer, if a debugger is not attached don't bother with the messages
    /// </summary>
    /// <param name="msg">the message we want</param>
    [Conditional("DEBUG")]
    public static void DebugMsg(string msg)
    {
        if (Debugger.IsAttached)
        {
            double secondsNow = (DateTime.Now - startTime).TotalMilliseconds / 1000;
            double secondsSinceLastTime = secondsNow - lastSeconds;
            lastSeconds = secondsNow;
            Debug.WriteLine($"{Thread.CurrentThread.ManagedThreadId:D2} {secondsNow,6:F3}(+{secondsSinceLastTime,4:F3})>>> " + msg);
        }
    }

    /// <summary>
    /// Conditional diagnostic messages for the programmer, if a debugger is not attached don't bother with the messages
    /// </summary>
    /// <param name="assertion">the boolean to be checked - emit a message if it is false</param>
    /// <param name="msg">the message we want</param>
    [Conditional("DEBUG")]
    public static void DebugAssert(bool assertion, string msg)
    {
        if (!assertion)
            DebugMsg(msg);
    }

    // 
    /// <summary>
    /// A mechanism for displaying an action sheet in a way that lets testing intercept it
    /// </summary>
    /// <param name="title">The title of the requested dialog</param>
    /// <param name="cancel">The cancel button text</param>
    /// <param name="buttons">The options to offer</param>
    /// <returns></returns>
    public delegate Task<string> DisplayActionSheetAsyncType(string title, string cancel, params string[] buttons);
    public static Task<string> ActualDisplayActionSheetAsync(string title, string cancel, params string[] buttons)
    {
        DebugMsg("Action Sheet to user: " + title);
        return MainThread.InvokeOnMainThreadAsync(() => Shell.Current.DisplayActionSheet(title, cancel, null, FlowDirection.MatchParent, buttons));
    }
    public static DisplayActionSheetAsyncType DisplayActionSheetAsync = ActualDisplayActionSheetAsync;

    /// <summary>
    /// Ask a simple yes/no question in a way that lets testing intercept it by assigning to AskAsync
    /// </summary>
    /// <param name="title">The title of the requested dialog</param>
    /// <param name="message">The message body to show</param>
    /// <param name="accept">Text for a 'yes' answer</param>
    /// <param name="cancel">Text for a 'no' answer</param>
    /// <returns></returns>
    public delegate Task<bool> AskAsyncType(string title, string message, string accept = null, string cancel = null);
    public static AskAsyncType AskAsync = ActualAskAsync; 
    public static async Task<bool> ActualAskAsync(string title, string message, string accept = null, string cancel = null)
    {
        DebugMsg("Question to user: " + message);
        return await MainThread.InvokeOnMainThreadAsync(() => Shell.Current.DisplayAlert(title, message, accept, cancel));
    }
    
    /// <summary>
    /// Send a message to the user and wait for acknowledgment
    /// </summary>
    /// <param name="title">The title of the requested dialog</param>
    /// <param name="message">The message body to show</param>
    /// <param name="accept">Text to acknowledge the message</param>
    /// <returns></returns>
    public delegate Task DisplayAlertAsyncType(string title, string message, string accept = null);
    public static DisplayAlertAsyncType DisplayAlertAsync = ActualDisplayAlertAsync;
    public static Task ActualDisplayAlertAsync(string title, string message, string accept = null)
    {
        DebugMsg("Alert to user: " + message);
        return MainThread.InvokeOnMainThreadAsync(() => Shell.Current.DisplayAlert(title, message, "OK"));
    }

    /// <summary>
    /// Show a summary of payments (basically enough information for a credit slip)
    /// </summary>
    /// <param name="paymentsViewModel">A <see cref="PaymentsViewModel"/> populated with the required payment information</param>
    /// <returns></returns>
    public static async Task ShowPayments(PaymentsViewModel paymentsViewModel)
    {
        await Shell.Current.ShowPopupAsync(new Views.PaymentsPage(paymentsViewModel));
    }
    /// <summary>
    /// Show an application message that will go away by itself if not acknowledged
    /// </summary>
    /// <param name="message">The message to show the user</param>
    /// <returns></returns>
    public static Task ShowAppSnackBarAsync(string message)
    {
        DebugMsg("Snack message to user: " + message);
        return Shell.Current.ShowPopupAsync(new Views.AppSnackBarPage(message));
    }
    
    /// <summary>
    /// Check if the contents of two files are the same
    /// </summary>
    /// <param name="path1">Path to first file</param>
    /// <param name="path2">Path to second file</param>
    /// <returns></returns>
    public static bool AreFileContentsEqual(String path1, String path2) =>
        File.Exists(path1) && File.Exists(path2)
        && File.ReadAllBytes(path1).SequenceEqual(File.ReadAllBytes(path2));

    /// <summary>
    /// Diagnostic list of image files - handy on Android where they are in an encrypted folder
    /// </summary>
    /// <param name="comment"></param>
    [Conditional("DEBUG")]
    public static void LogImageFolder(string comment)
    {
        DebugMsg($"Image Folder contents ({comment}):");
        foreach (string filePath in Directory.EnumerateFiles(Meal.ImageFolderPath))
            DebugMsg($"    {filePath} - length {new FileInfo(filePath).Length}, created {File.GetCreationTime(filePath)}");
        DebugMsg($"End of Image Folder contents");
    }

    [Conditional("DEBUG")]
    private static void LogFolderTree(string comment)
    {
        int indent = 3;
        void listSubdirectories(string parentPath)
        {
            indent += 3;
            foreach (string path in Directory.EnumerateDirectories(parentPath))
            {
                DebugMsg(new string(' ', indent) + $"{path} - created {Directory.GetCreationTime(path)}");
                listSubdirectories(path);
            }
            indent -= 3;
        }

        string folder = App.BaseFolderPath;
        DebugMsg($"DivisiBill Folder tree ({comment}):");
        if (Directory.Exists(folder))
        {
            DebugMsg($"   {folder} - created {Directory.GetCreationTime(folder)}");
            listSubdirectories(folder);
        }
        else
            DebugMsg($"Folder {folder} not found");
    }

    /// <summary>
    /// Continue after the specified (or default) time, whether the task has finished or not
    /// </summary>
    /// <param name="task">The task that is being run</param>
    /// <param name="millisecondsTimeout">how long to wait for it to finish before continuing</param>
    /// <returns></returns>
    public static Task OrDelay(this Task task, int millisecondsTimeout = 15000) => Task.WhenAny(task, Task.Delay(millisecondsTimeout));
    public static string ApproximateAge(DateTime dt)
    {
        string s = string.Empty;
        bool MakeText(double amount, string unit)
        {
            amount = Math.Round(amount, 1);
            if (amount < 1)
                return false;
            if (amount == 1)
                s = "1 " + unit;
            else if (amount < 10)
                s = amount.ToString() + " " + unit + "s";
            else
                s = string.Format($"{amount:F0} {unit}s"); // amount.ToString() + " " + unit + "s";
            return true;
        }
        TimeSpan age = DateTime.Now - dt;
        double Years = age.TotalDays / 365.25;
        double Days = age.TotalDays;
        double Hours = age.TotalHours;
        double Minutes = age.TotalMinutes;
#pragma warning disable CS0642 // Possible mistaken empty statement
        if (MakeText(Years, "year")) ;
        else if (MakeText(Days, "day")) ;
        else if (MakeText(Hours, "hour")) ;
        else if (MakeText(Minutes, "minute")) ;
#pragma warning restore CS0642 // Possible mistaken empty statement
        if (!string.IsNullOrEmpty(s))
            s = "(" + s + ")";
        return s;
    }
    /// <summary>
    /// Given a DateTime value return a string which uses it as a name. DateTimeFromName is the inverse of this method.
    /// </summary>
    public static string NameFromDateTime(DateTime dateTime) => dateTime.ToString("yyyyMMddHHmmss");
    public static bool TryDateTimeFromName(string name, out DateTime dateTime)
    {
        string s = Path.GetFileNameWithoutExtension(name);
        if (s.Length == 14
            && int.TryParse(s.Substring(0, 4), out int y)
            && y > 2010 && y < 2030
            && int.TryParse(s.Substring(4, 2), out int m)
            && m >= 1 && m <= 12
            && int.TryParse(s.Substring(6, 2), out int d)
            && d >= 1 && d <= 31
            && int.TryParse(s.Substring(8, 2), out int hh)
            && hh >= 0 && hh <= 23
            && int.TryParse(s.Substring(10, 2), out int mm)
            && mm >= 0 && mm < 60
            && int.TryParse(s.Substring(12, 2), out int ss)
            && ss >= 0 && ss < 60)
        {
            dateTime = new DateTime(y, m, d, hh, mm, ss); // Plausible date
            return true;
        }
        else
        {
            dateTime = DateTime.MinValue;
            return false;
        }
    }
    public static DateTime DateTimeFromName(string name)
    {
        string s = Path.GetFileNameWithoutExtension(name);
        if (s.Length == 14
            && int.TryParse(s.Substring(0, 4), out int y)
            && y > 2010 && y < 2030 
            && int.TryParse(s.Substring(4, 2), out int m)
            && m >= 1 && m <= 12 
            && int.TryParse(s.Substring(6, 2), out int d)
            && d >= 1 && d <= 31
            && int.TryParse(s.Substring(8, 2), out int hh)
            && hh >= 0 && hh <= 23 
            && int.TryParse(s.Substring(10, 2), out int mm)
            && mm >= 0 && mm < 60 
            && int.TryParse(s.Substring(12, 2), out int ss)
            && ss >= 0 && ss < 60)
            return new DateTime(y, m, d, hh, mm, ss); // Plausible date
        else
            return DateTime.MinValue;
    }
    public static bool WithinOneSecond(DateTime t1, DateTime t2) => Math.Abs((t1 - t2).TotalMilliseconds) < 1000;

    private static Regex JsonDateRegex = new Regex(@"^/Date\((\d+)(-\d{2})(\d{2})\)/$");

    /// <summary>
    /// Parse a Json time serialized by a DataContractJsonSerializer returning a DateTimeOffset 
    /// </summary>
    /// <param name="myJson">the JSON derived string, what's expected is in the form /Date(1389480362030-0800)/
    /// Note that the slashes are part of the string the big number is milliseconds since unix epoch and the 
    /// small one is the time zone offset in HHMM form</param>
    /// <param name="dateTimeOffset"></param>
    /// <returns></returns>
    public static bool TryParseJsonDate(string myJson, out DateTimeOffset dateTimeOffset)
    {

        Match match = JsonDateRegex.Match(myJson);

        if (match.Success)
        {
            if (double.TryParse(match.Groups[1].Value, out double unixTs)
                && int.TryParse(match.Groups[2].Value, out int offsetHH)
                && int.TryParse(match.Groups[3].Value, out int offsetMM))
            {
                int tzOffset = offsetHH >= 0 ? offsetHH * 60 + offsetMM : offsetHH * 60 - offsetMM;

                DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified);
                dateTimeOffset = new DateTimeOffset(epoch.AddMilliseconds(unixTs) + TimeSpan.FromMinutes(tzOffset), TimeSpan.FromMinutes(tzOffset));
                return true;
            }
        }
        dateTimeOffset = DateTimeOffset.MinValue;
        return false;
    }

    /// <summary>
    /// A string extension method to return a string which fits in a specified number of characters, adding ellipses if it is necessary to truncate it 
    /// </summary>
    /// <param name="fullString">The string to be fitted</param>
    /// <param name="maxLen">the length within which it should fit</param>
    /// <returns></returns>
    public static string TruncatedTo(this string fullString,int maxLen)
    {
        if (fullString.Length <= maxLen)
            return fullString;
        if (maxLen <= 3)
            return "...";
        return fullString.Substring(0, Math.Min(fullString.Length-1, maxLen-3)) + "...";
    }

    /// <summary>
    /// Determine if two strings are either equal or both NullOrEmpty
    /// </summary>
    /// <param name="s1">string to compare</param>
    /// <param name="s2">other string to compare</param>
    /// <returns>true if the strings are functionally equal, false if they differ</returns>
    public static bool StringFunctionallyEqual(string s1, string s2) => string.Equals(s1, s2) || (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2));
    public static async Task HapticNotify()
    {
        try
        {
            HapticFeedback.Perform(HapticFeedbackType.LongPress);
            await Task.Delay(200);
            HapticFeedback.Perform(HapticFeedbackType.LongPress);
            await Task.Delay(200);
            HapticFeedback.Perform(HapticFeedbackType.LongPress);
        }
        catch (FeatureNotSupportedException)
        {
            // Feature not supported on device
        }
        catch (Exception)
        {
            // Other error has occurred.
        }
    }

    public static void EnumerateInheritance(object o)
    {
        var objectType = o.GetType();
        while (objectType is not null)
        {
            DebugMsg(objectType.ToString());
            objectType = objectType.BaseType;
        }
    }

    // From stack overflow discussion at https://stackoverflow.com/questions/1600962/displaying-the-build-date
    public static async Task InitializeUtilitiesAsync()
    {
        using var notesStream = await FileSystem.OpenAppPackageFileAsync("Release Notes.html");
            using (var reader = new StreamReader(notesStream))
                ReleaseNotes = new HtmlWebViewSource { Html = reader.ReadToEnd() };
    }
    public static string CurrencySymbol = System.Globalization.NumberFormatInfo.CurrentInfo.CurrencySymbol;
    public static string EditionName => App.IsLimited ? "Basic" : "Professional";
    private static Version assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
    public static string VersionName { get; } = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
    public static string DebugString { get; } = IsDebug ? "DEBUG" : null;
    public static string Revision { get; } = assemblyVersion.Revision.ToString();
    public static string BuildTime { get; } = DateTime.Parse(BuildEnvironment.BuildTimeString, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime().ToString();
    private static HtmlWebViewSource GetReleaseNotes()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream("DivisiBill.Release Notes.html");
        using (var reader = new StreamReader(stream))
            return new HtmlWebViewSource { Html = reader.ReadToEnd() };
    }
    public static HtmlWebViewSource ReleaseNotes { get;  private set; }
    #region Geo
    public const int close = 5; // Anything within this distance is just 'close' because GPS is usually less certain then this 

    public static int GetDistanceTo(this Location location1, Location location2)
    {

        int i = location1.IsAccurate() && location2.IsAccurate()
            ? (int)Math.Round(Location.CalculateDistance(location1, location2, DistanceUnits.Kilometers) * 1000)
            : Distances.Inaccurate;
        return i;
    }

    /// <summary>
    /// Copy the few of basic location elements we care about
    /// </summary>
    /// <param name="location">the destination of the copy</param>
    /// <param name="sourceLocation">the source of the copy</param>
    public static void CopyFrom(this Location location, Location sourceLocation)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (sourceLocation is null)
            location = null;
        else
        {
            location.Latitude = sourceLocation.Latitude;
            location.Longitude = sourceLocation.Longitude;
            location.Accuracy = sourceLocation.Accuracy;
        }
    }
    public static bool IsValid(this Location location) => location is not null && App.UseLocation && location.Accuracy <= Distances.AccuracyLimit;
    public static bool IsAccurate(this Location location) => (location is not null && location.Accuracy.HasValue);
    /// <summary>
    /// This is accuracy, but as an integer (not double) number of meters or an "inaccurate" value
    /// </summary>
    /// <param name="location">The Location object to be evaluated</param>
    /// <returns>The distance from the current location, or an "inaccurate" value</returns>
    public static int AccuracyOrDefault(this Location location) => location.IsAccurate() ? (int)Math.Round(location.Accuracy.Value) : (Distances.Inaccurate);
    public static string MakeLocationText(Location location) => location is null || !location.IsValid() ? null :
                MakeLocationText(location.Latitude, location.Longitude, location.AccuracyOrDefault());

    public static string MakeLocationText(double Latitude, double Longitude, int HorizontalAccuracy)
    {
        char EW = Math.Sign(Longitude) < 0 ? 'W' : 'E',
            NS = Math.Sign(Latitude) < 0 ? 'S' : 'N';
        return AdjustedString(Math.Abs(Latitude), HorizontalAccuracy) + NS + ", "
            + AdjustedString(Math.Abs(Longitude), HorizontalAccuracy) + EW + " ± " + HorizontalAccuracy + "m";
    }
    public static string AdjustedString(double d, double accuracy)
    {
        // The earth is about 40,000 km in circumference and there are 360 degrees of arc in a circle
        // So 1 degree is roughly 111,111 and that means 0.0001 degrees is about 11 meters
        string fmt;
        if (accuracy < 11)
            fmt = "0000";
        else if (accuracy < 111)
            fmt = "000";
        else if (accuracy < 1111)
            fmt = "00";
        else if (accuracy < 11111)
            fmt = "0";
        else
            fmt = "";
        return String.Format("{0:0." + fmt + "}", d);
    }
    public static double Adjusted(double d, double accuracy)
    {
        // The earth is about 40,000 km in circumference and there are 360 degrees of arc in a circle
        // So 1 degree is roughly 111,111 and that means 0.0001 degrees is about 11 meters
        if (accuracy < 11)
            return Math.Round(d, 4);
        else if (accuracy < 111)
            return Math.Round(d, 3);
        else if (accuracy < 1111)
            return Math.Round(d, 2);
        else if (accuracy < 11111)
            return Math.Round(d, 1);
        else
            return Math.Round(d);
    }
#endregion
}
public static class Distances
{
    public const int Inaccurate = 99999999; // The circumference of the earth is just over 40,000,000 m, this is more 
    public const int Unknown = Inaccurate + 1; // The circumference of the earth is just over 40,000,000 m, this is more 
    public const int Close = 100; // Anything within 100 meters is just 'close' 
    public const int AccuracyLimit = // In meters, no location less accurate than this is acceptable
#if WINDOWS
        8000; // Because the desktop location accuracy without GPS is 7990
#else
        1000; // 1000 meters seems like a reasonable limit 
#endif
    /// <summary>
    /// Round the distance to something it makes sense to compare:
    ///    close is changed to a single value
    ///    under 10 km is rounded to 100 meters (so 1 decimal place in km)
    ///    everything inaccurate is changed to a single (large) value
    ///    most 'normal' distances arr rounded to the nearest km
    /// </summary>
    /// <param name="distance"></param>
    /// <returns>the modified distance value</returns>
    public static int Simplified(int distance) => 
        distance <= Close ? Close
        : (distance < 1000 ? distance
        : (distance >= Inaccurate ? Inaccurate 
        : (distance + 50) / 100 * 100));
    /// <summary>
    /// Show distance text
    /// </summary>
    /// <param name="distance"></param>
    /// <returns>String representation of approximate distance and units</returns>
    public static string Text(int distance) =>
        distance <= Close ? "close"
        : (distance < 1000 ? string.Format("{0} m",distance)
        : (distance < 9950 ? string.Format("{0:F1} km", (distance)/1000.0)
        : (distance >= Inaccurate ? ""
        : string.Format("{0} km", (distance+499) / 1000) ) ) );
}
/// <summary>
/// From https://stackoverflow.com/questions/7863573/awaitable-task-based-queue
/// </summary>
/// <typeparam name="T">The type of object to be placed in the queue</typeparam>
public class AwaitableQueue<T>
{
    private readonly SemaphoreSlim semaphore = new SemaphoreSlim(0);
    private readonly object queueLock = new object();
    private readonly Queue<T> queue = new Queue<T>();

    public void Clear()
    {
        lock (queueLock)
        {
            queue.Clear();
        }
    }

    public void Enqueue(T item)
    {
        lock (queueLock)
        {
            queue.Enqueue(item);
            semaphore.Release();
        }
    }

    public async Task<T> DequeueAsync(CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        lock (queueLock)
        {
            return queue.Dequeue();
        }
    }
}
/// <summary>
/// Class invoked whenever a Sentry transaction is to be sent
/// </summary>
public class SentryEventProcessor : ISentryEventProcessor
{
    public static int skipBreaks = 0; // Just set this to skip the next however many breaks
    public  SentryEvent Process(SentryEvent sentryEvent)
    {
        Utilities.DebugMsg($"In SentryEventProcessor.Process, Sentry EventId: {sentryEvent.EventId}");
        if (Utilities.IsDebug)
        {
            // Never report anything on a debug build but you can put a breakpoint here to look at them
            Utilities.DebugMsg("In SentryEventProcessor.Process, debug build, ignoring Sentry Event");
            if (skipBreaks <= 0)
                Debugger.Break();
            else
                skipBreaks --;
            return null;
        }
        if (App.Settings is not null && App.Settings.SendCrashYes)
            return sentryEvent; // this is on a separate line so it's east to use in "set next statement" 
        return null;
    }
}

/// <summary>
/// Simple thread safe counter, sealed class as it's not designed to be inherited from.
/// </summary>
public sealed class Counter
{
    private volatile int current = 0;

    // update the method name to imply that it returns something.
    public int Increment() =>
        // prefix fields with 'this'
        Interlocked.Increment(ref current);
    public int Decrement() =>
        // prefix fields with 'this'
        Interlocked.Decrement(ref current);
    public void Reset() => current = 0;
}
