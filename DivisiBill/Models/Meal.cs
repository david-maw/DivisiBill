using DivisiBill.Services;
using System.Diagnostics;

namespace DivisiBill.Models;

/// <summary>
/// Meals and their lifetimes.
/// <para>Note that the terms meal and bill are synonymous, for historical reasons the objects are called meals
/// internally but public documentation calls them bills.</para>
/// <para>The objective of the bill lifetime management algorithm is to store bills at a logical time rather than
/// have an explicit 'save' operation. The idea being that the user starts with some bill (either a default 
/// one, or an old one), makes changes to it, looks at the result, then shuts down the app until next time they
/// have a bill to process. At the same time we want to lose as little data as possible if the application halts
/// abruptly (like during an application failure or a system reboot).</para>
/// <para>Main use cases:</para>
/// <list type="number">
/// <item>
/// <description>A user enters a bill, does nothing for a while, then reuses the same bill for a different event.
/// This scenario should trigger saving the old version of the bill <see cref="MarkAsChanged"/>.</description>
/// </item>
/// <item>
/// <description>A user enters a bill, pauses for a while, then replaces it with a stored bill.
/// This should trigger saving the first bill again <see cref="MarkAsChanged"/>.</description>
/// </item>
/// <item>
/// <description>A user loads a stored bill, then replaces it with a stored bill without changing anything.
/// This should not trigger a new bill to be stored because  <see cref="MarkAsChanged"/> will not have been called.</description>
/// </item>
/// <item>
/// <description>A user loads a stored bill, then edits it.
/// This should do nothing except a periodic save for safety (see below) unless something critical (like the venue name)
/// is changed but does lead to case 1 or 2.</description>
/// </item>
/// <item>
/// <description>A user loads a stored bill, edits it then pauses (perhaps to actually eat a meal).
/// After the pause of many minutes they scan a bill image and apply it to the prepared bill. This should 
/// do nothing except a periodic save for safety (see below) but does lead to case 1 or 2.</description>
/// </item>
/// </list>
/// <para>To sum up, a bill is not final ("Frozen") until we are sure it is finished with, 
/// and that only happens when more than 90 minutes (App.MinimumIdleTime) have elapsed
/// and something important (like the venue name) changes. After an additional hour (App.MaximumIdleTime) has 
/// elapsed since the bill was last changed any subsequent change represents a new bill. The current bill is evaluated
/// only occasionally (currently when the app initializes, when a bill is loaded, when items from a scanned bill 
/// are inserted or when a venue is changed - look for uses of <see cref="MarkAsNewAsync"/> to see this).
/// </para><para>
/// Put another way, you can change a bill as often as you like for the first 
/// portion of its life and we'll always assume it's just the same bill being edited and won't store it.
/// After that, venue changes or loading another bill will trigger storing the current bill (if it has been changed). 
/// After the bill is <see cref="App.MaximumIdleTime"/> old a scan at program restart will cause it to be persisted and marked as <see cref="Frozen"/>, 
/// after that it can still be viewed but any attempt to change it simply results in a
/// a new bill for the same venue is created. Even if the program is not restarted, if a bill goes 10 
/// minutes without an update it will be checked to see if it has aged out and, if necessary, frozen so a new one will be started
/// by the next update.</para>
/// 
/// <para>In order to protect against data loss in an unexpected restart, application reinstallation or removal and 
/// reinstall, changed bills are periodically backed up to 
/// the application dictionary (by <see cref="PeriodicSaveAsync"/>), to 'disk', and, if it is allowed, to the cloud 
/// (both by <see cref="Saver.TimedLoop"/>). All the methods just loop checking for updates periodically. Otherwise
/// an immediate backup can be triggered by calling <see cref="RequestSnapshot"/> because something important (like 
/// a Venue name) changed.</para>
/// 
/// <para>The user can also choose to archive the current state to a file at any time, which can be especially handy
/// if you want to manually port the data to another system..</para>
/// 
/// <para>The implementation is that there are a list of meals (bills) stored locally in XML files and, optionally, 
/// images (in JPG files) a list of those files is in <see cref="LocalMealList"/> which has pointers to a MealSummary
/// for each meal. Each MealSummary includes the name of the file it is in and the name of the image if there is one. All the 
/// meals are in that list, including the current meal.</para>
/// 
/// <para>The current meal is stored in the application dictionary - it may also be stored on disk, but might not be.
/// For example it is persisted to disk when the program exits and periodically if it changes. The same
/// bill will be reloaded when the program next starts, although it may be marked as <see cref="Frozen"/> depending on how old it is.</para>
/// <para>Any change to a Frozen bill results in it being persisted to local storage (aka disk) if it has unsaved changes
/// but either way a new copy made (with a new creation time) for subsequent updates.</para>
/// 
/// <para>Implementation Details:</para>
/// <para>The files representing persisted Meal objects are stored in different folders for debug builds, so from outside you see
/// a DivisiBill or DivisiBillDebug folder and within it are Meals and Images folders containing Meals and their images
/// respectively. Android uses an encrypted app-private folder (/data/user/0/com.autoplus.divisibill/files) in Windows it's 
/// an unencrypted folder in the user's Documents folder, making debugging on Windows much easier than Android.</para>
/// <para>So there's always exactly one current meal, the question is when to persist it to a new file, in 
/// other words when is it a distinct bill, and when is it an existing one you've updated some more (case 4 above).
/// Initially a meal is marked as <see cref="SavedToApp"/> and <see cref="SavedToFile"/> true and whenever anything significant is done 
/// to it, the bill is marked as <see cref="SavedToApp"/> and <see cref="SavedToFile"/> false. When certain actions are performed we check whether
/// it has been marked changed (SavedToApp or SavedToFile false) and, if it has, we persist the file in XML to
/// either the app dictionary or a file, or both (done by calling SaveIfChangedAsync) this is also one of 
/// the opportunities to see if it is appropriate to save to disk the version of the meal preceding the change
/// by calling <see cref="MarkAsNewAsync"/> and passing a parameter to say why it seemed worthwhile to save a snapshot of the meal.</para>
/// <para>Once a meal is saved, the current meal is marked as Frozen and unchanged and the name of the file it is 
/// stored in is saved (mostly for historical reasons, this algorithm used to be different). If a frozen meal
/// is marked as changed, the meal <see cref="CreationTime"/> is set as well as resetting Frozen and changing the storage file name
/// (which is derived from the <see cref="CreationTime"/>).</para>
/// <para>The very first time the program runs there won't be a stored bill, so we create one and mark it as SavedToApp and
/// SavedToFile = true and IsDefault - such a bill never needs storing to a permanent file until it is changed (just 
/// setting <see cref="Frozen"/> would do, but <see cref="IsDefault"/> allows for a check at storage time).</para>
/// <para>Several attributes control all this, the app has a variety of settings it uses to remember the status of the stored bill
/// <see cref="SavedToApp"/>.</para>
/// <para>Each meal itself has a variety of relevant properties:</para>
/// <list type="bullet">
/// <item><description><see cref="Summary"/> - a reference to the MealSummary for this Meal</description></item>
/// <item><description><see cref="SavedToFile"/> - means it has been persisted to local storage (in a file named according to the
/// CreationTime of the bill) since the last time it was updated.</description></item>
/// <item><description><see cref="MealSummary.IsLocal"/> - meaning this MealSummary represents a Meal stored in a local file, possibly not the
/// latest version (SavedToFile is what indicates that)</description></item>
/// <item><description><see cref="CreationTime"/> - when it was created (the time when a frozen meal was first changed)</description></item>
/// <item><description><see cref="LastChangeTime"/> - when it was most recently changed (the time when a bill was last changed)</description></item>
/// <item><description><see cref="Frozen"/> - means we've persisted a copy, so a new bill needs to be created next time the bill is changed</description></item>
/// <item><description><see cref="IsDefault"/> - is it an unmodified sample meal the program created - this never needs to be saved</description></item>
/// <item><description><see cref="SavedToApp"/> - means it was not changed since last being persisted to the app dictionary</description></item>
/// <item><description><see cref="TooOldToContinue"/> - This basically says an existing bill that is about to be updated is really a new bill for 
/// the same venue.</description></item>
/// </list>
/// <para>A key method is <see cref="MarkAsNewAsync"/> which flags a meal as having been recreated (it is created with changed false), this may 
/// cause the old version to be saved to a file if it has unsaved changes (SavedToFile is false). There are
/// several reasons to do this:</para>
/// <list type="number">
/// <item><description>The current bill is so old it must be saved before making updates, this only happens
/// just after starting the program.</description></item>
/// <item><description>The Venue Name has been changed, so it's obviously now a bill for a new location</description></item>
/// <item><description>The current bill is being replaced with an old one</description></item>
/// <item><description>It has been a while since the last update
/// If a bill is old enough for changes to be stored as a new bill (default 15 minutes) when <see cref="MarkAsNewAsync"/> is
/// called it is marked as Frozen, so any subsequent change will start a new bill.</description></item>
/// </list>
/// <para>Other methods which relate to bill lifetime management are:</para>
/// <list type="bullet">
/// <item><description><see cref="MarkAsChanged"/> - Mark the current meal as changed or if it is frozen create a new bill from it.</description></item>
/// <item><description><see cref="SaveIfChangedAsync"/> - if the meal has changed, then save it (to the app dictionary, local and/or 
/// remote storage)</description></item>
/// <item><description><see cref="MealSummary.LocationChanged"/> - if a meal has been saved add it to the <see cref="LocalMealList"/>
/// or <see cref="RemoteMealList"/> as appropriate.
/// so it is visible</description></item>
/// </list>
/// <para>The general idea is that we flag a bill as changed whenever something which would be persisted changes in the bill
/// and call SaveIfChanged periodically, so if the app, or system, crashes, you'll be able to recover from a recent point. 
/// Occasionally, we save the current bill to the cloud, just in case a real catastrophe 
/// happens and all local bills are lost (as of Android 30 this can happen if you uninstall the app).</para>
/// <para>It is important NOT to mark bills as changed when values which are not persisted change, so that, for example, changing the
/// subtotal on a newly loaded bill has no effect, but changing an item on the bill does, so in practice, all significant
/// changes are persisted.</para>
/// <para>Meal images are handled as distinct files, the most recent image, if there is one, is always in a file
/// named like the Meal file, but with a JPG extension instead of XML. As of 2022 image processing is used to shrink 
/// images but they are still 10s of kB so they are much larger than Meal files which are typically 2kB or less. For this reason
/// an Archive operation saves bills but images are optional.</para>
/// </summary>

[DebuggerDisplay("{DebugDisplay}")]
public partial class Meal : ObservableObjectPlus
{
    // The class is declared in multiple files called "meal. ... .cs" broadly delimited by functionality. 
}
