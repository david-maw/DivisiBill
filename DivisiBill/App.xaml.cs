using CommunityToolkit.Maui.Core;
using DivisiBill.Models;
using DivisiBill.Services;
using Microsoft.Maui.Handlers;
using System.ComponentModel;
using System.Diagnostics;

namespace DivisiBill;

public partial class App : Application, INotifyPropertyChanged
{
    #region Global Variables and Constants
    #region Build time feature availability checks
    // Without web services we cannot do licensing or OCR
    public static readonly bool WsUriDefined = !string.IsNullOrWhiteSpace(Generated.BuildInfo.DivisiBillWsUri);
    // Sentry us used in production to report problems
    public static readonly bool SentryAllowed = !string.IsNullOrWhiteSpace(Generated.BuildInfo.DivisiBillSentryDsn);
    #endregion
    internal static bool UseLocation = true;
    /// <summary>
    /// Indicates whether a license was able to be checked, the check itself may or may not have worked.
    /// </summary>
    internal static bool LicenseChecked = false;
    /// <summary>
    /// IsLimited is the inverse of whether Professional Edition has been purchased, so it may be set but is rarely reset.
    /// The normal scenario is that a person uses the Basic Edition then buys the Professional Edition so at the point
    /// where they buy it they cannot have created any cloud based backups and the fact we would not attempt to recover
    /// them during initialization does not matter.
    /// 
    /// If Basic Edition is uninstalled the state (including saved Meals, Venues and People) is lost unless manually archived.
    /// If an instance of Professional Edition is uninstalled, Meals, Images, Venues and People should have been backed up to
    /// the cloud and only program options (see <see cref="Settings"/>) will be lost.
    /// </summary>
    internal static bool IsLimited = true; // Whether capabilities are limited, set in initialization
#if DEBUG
    public const string BaseFolderName = "DivisiBillDebug";
#else
    public const string BaseFolderName = "DivisiBill";
#endif
    public static readonly TimeSpan MinimumIdleTime = TimeSpan.FromMinutes(90); // A changed bill younger than this is not persisted
    public static readonly TimeSpan MaximumIdleTime = TimeSpan.FromMinutes(150); // Changed bills untouched for this long are always persisted
    internal static string BaseFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), BaseFolderName);
    // On Android BaseFolderPath is typically /data/user/0/com.autoplus.divisibill/files/DivisiBill, on Windows C:\users\<user>\Documents\DivisiBill
    internal static string AppFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), BaseFolderName);
    // The AppFolderPath is silently mapped into something App specific, for example 
    //   ..AppData\Local\Packages\D9049CD2-5037-432D-BC7E-2E2FB39EBA1C_9zz4h110yvjzm\LocalCache\Local\DivisiBillDebug
    // The 'magic number' is the package.appxmanifest 'package family name'.
    internal static Location? MyLocation;
    internal static Location? GpsLocation; // The most recent location the GPS returned
    private static Task? LocationMonitorTask;
    private static CancellationTokenSource LocationMonitorCancellationTokenSource = new();
    internal static CancellationTokenSource SaveProcessCancellationTokenSource = new();
    /// <summary>
    /// CancellationTokenSource used to cancel the license checking process.
    /// This token source allows for cooperative cancellation of the license checking task.
    /// This would enable the application to stop the license verification process gracefully should it need to.
    /// </summary>
    internal static readonly CancellationTokenSource LicenseProcessCancellationTokenSource = new();
    public static TaskCompletionSource<bool> InitializationComplete { get; set; } = new();// Allows processes to wait for initialization to complete before doing things that might interfere with it, such as persistence during shutdown
    public static readonly PauseTokenSource IsRunningSource = new();
    public static readonly PauseTokenSource CloudAllowedSource = new();
    internal static CancellationTokenSource RequestBackupLoopStop = new();
    internal static Task? MainBackupLoopTask;
    internal static bool IsTesting = AppDomain.CurrentDomain.FriendlyName.Equals("testhost");
    internal static int ScanOption = 2;
    internal static bool pauseInitialization = false;
    internal static bool isTutorialMode = false;
    private const int WindowWidth = 600;
    private const int WindowHeight = 1200;
    #endregion
    #region Initialization
    public App()
    {
        Utilities.DebugMsg("App constructor entered");
        InitializeComponent();
        DebugInitialize();
#if WINDOWS
        // Do not show on/off text with switch, see https://github.com/dotnet/maui/issues/6177
        SwitchHandler.Mapper.AppendToMapping("Custom", (h, v) =>
            {
                // Get rid of On/Off label beside switch, to match other platforms
                h.PlatformView.OffContent = string.Empty;
                h.PlatformView.OnContent = string.Empty;
                h.PlatformView.MinWidth = 40;
                //h.PlatformView.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                //h.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(3);
            });
#endif
        // Change all Entry controls to auto-select text
        ModifyEntry();
        // Enable connectivity monitoring
        Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;
#if ANDROID
        DivisiBill.Platforms.Android.StreamDispatcher.Activated += StreamDispatcher_Activated;
#elif WINDOWS
        DivisiBill.Platforms.Windows.StreamDispatcher.Activated += StreamDispatcher_Activated;
#endif
    }

    ~App()
    {
        Connectivity.ConnectivityChanged -= Connectivity_ConnectivityChanged;
    }

    public static new App Current => Application.Current as App ?? throw new InvalidOperationException("Application.Current is not of type App");

    /// <summary>
    /// Update all Entry controls so they initially select all text when focused
    /// </summary>
    private static void ModifyEntry() => EntryHandler.Mapper.AppendToMapping("MyCustomization", (handler, view) =>
        {
#if ANDROID
            handler.PlatformView.SetSelectAllOnFocus(true);
#elif IOS || MACCATALYST
            handler.PlatformView.EditingDidBegin += (s, e) =>
            {
                handler.PlatformView.PerformSelector(new ObjCRuntime.Selector("selectAll"), null, 0.0f);
            };
#elif WINDOWS
            handler.PlatformView.GotFocus += (s, e) =>
            {
                handler.PlatformView.SelectAll();
            };
#endif
        });
    #endregion
    #region Lifecycle and window management
    #region Persist Changes
    private static async void PersistAsNeeded() => await MainThread.InvokeOnMainThreadAsync(ActualPersistAsNeeded);

    /// <summary>
    /// Persists application state and settings if necessary. This method can be called multiple times safely and will
    /// only perform persistence operations when initialization is complete.
    /// </summary>
    /// <remarks>This method updates the last use timestamp and saves venue and meal settings if changes are
    /// detected. Exceptions during persistence are ignored to ensure the application can continue shutting down or
    /// performing other operations without interruption.</remarks>
    private static async void ActualPersistAsNeeded()
    {
        Utilities.DebugMsg($"In App.PersistAsNeeded; initialization completed = {InitializationComplete.Task.IsCompleted}");
        if (!InitializationComplete.Task.IsCompleted)
            return; // There's no knowing what state we're in, so don't do anything
        Settings.LastUse = DateTime.Now; // Note when we last did anything
        try
        {
            if (!Venue.IsSaved)
                await Venue.SaveSettingsAsync();
        }
        catch (Exception)
        {
            // Just ignore it and go on to the next operation as we are stopping anyway
        }
        // person data is already updated; it is saved whenever it changes since that is relatively rare, so nothing to do there
        try
        {
            await Meal.CurrentMeal.SaveIfChangedAsync(SaveRemote: false); // save a snapshot if needed, but quickly, so no remote save
        }
        catch (Exception)
        {
            // Just ignore it and go on to the next operation as we are stopping anyway
        }
    }
    #endregion
    #region Application Launch
    private static string priorWhat = "unknown";

    /// <summary>
    /// Gets or sets a value indicating whether the App was initiated by an Android intent other than Intent.ActionMain (the
    /// regular application start intent).
    /// </summary>
    /// <remarks>This property is set to <see langword="true"/> only on Android platforms when the
    /// application is started via an intent. On other platforms, this property remains <see langword="false"/>.</remarks>
    public bool IsIntentLaunch { get; set; } = false; // Only set true on Android

    /// <summary>
    /// Handles the event when a stream is to be read, triggered by an Android Intent.
    /// This method initiates modal navigation to the appropriate page if DivisiBill is already running.
    /// </summary>
    /// <param name="stream">The stream containing the file data to be processed. Must not be null.</param>
    /// <param name="mimeType">The MIME type of the file represented by the stream. Used to identify the file format.</param>
    private async void StreamDispatcher_Activated(Stream stream, string mimeType)
    {
        try
        {
            Utilities.DebugMsg("In StreamDispatcher_Activated: Notification for a stream of type: " + mimeType);
            if (!IsIntentLaunch)
            {   // The app is already running so we have infrastructure enough to just push a modal page here
                Utilities.DebugMsg("App is already running, pushing IntentPage modally");
                await Shell.Current.Navigation.PushModalAsync(new Views.RestorePage());
            }
            else // Do the minimum required initialization
            {
                Utilities.DebugMsg("App is not currently running, so we must do some initialization");
                App.IsCloudAllowed = false; // We don't want to do anything that might involve the cloud
                // Create all the required folders, in case the app has never run before
                Meal.InitializeFolders();
                Person.InitializeFolders();
                Venue.InitializeFolders();
                // Load up the existing lists of people and venues, so they are available when we process the stream
                // Don't worry about meals because each is in its own file so there's no list to load
                Person.LoadFromLocal();
                await Venue.LoadFromLocal();
            }
        }
        catch (Exception ex)
        {
            Utilities.DebugMsg("Notification handling failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Provides a thread-safe queue of pending intents that can be awaited asynchronously. 
    /// </summary>
    /// <remarks>This queue allows consumers to enqueue and dequeue <see cref="StreamRequest"/> items in an
    /// asynchronous manner, enabling coordination between producers and consumers without blocking threads. The queue
    /// is read-only and should not be reassigned.</remarks>
    public readonly AwaitableQueue<StreamRequest> IntentQueue = new();

    /// <summary>
    /// Creates and returns the application's main window based on the current activation state.
    /// </summary>
    /// <remarks>On Android, if the application is launched via an intent, the returned window displays the
    /// intent-specific page. Otherwise, the standard main window is created. This method is called by the
    /// application framework during startup.</remarks>
    /// <param name="activationState">The activation state that provides context for window creation, including information about how the application
    /// was launched.</param>
    /// <returns>A Window instance representing the application's main user interface. The specific window returned depends on
    /// the activation state and platform launch context.</returns>
    protected override Window CreateWindow(IActivationState? activationState)
    {
#if ANDROID
        IsIntentLaunch = Platforms.Android.MainActivity.IsIntentLaunch;
#endif
        Utilities.DebugMsg($"In CreateWindow, IsIntentLaunch = {IsIntentLaunch}");

        return IsIntentLaunch
            ? new Window(new Views.RestorePage())
            : CreateMainWindow();
    }
    private static Window CreateMainWindow()
    {
        Window window = new(new AppShell());

        static bool IsRepeated(string what)
        {
            bool result = string.Equals(priorWhat, what);
            Utilities.DebugMsg("Main Window state = " + what + (result ? " (repeated)" : ", previously " + priorWhat));
            priorWhat = what;
            return result;
        }

        void StoreWindowLocation(double x, double y, double w, double h)
        {
            if (Utilities.IsWinUI && Settings is not null)
                Settings.InitialPosition = new Rect(x, y, w, h);
        }

        // Outer block of CreateWindow

        Utilities.DebugMsg("In CreateWindow, assigning events");

        window.Created += (s, e) =>
        {
            InitializationComplete = new(); // Flag initialization as incomplete until we finish it
            if (!IsRepeated("Created"))
                HandleActivityChanges(false);
        };

        window.Activated += async (s, e) =>
        {
            if (!IsRepeated("Activated"))
            {
                Utilities.DebugMsg($"In Window.Activated; initialization completed = {InitializationComplete.Task.IsCompleted}");
                if (InitializationComplete.Task.IsCompleted)
                {
                    if (LicenseChecked) // If we have checked licenses before, do it again, otherwise don't bother so as not to keep complaining about a bad connection
                        await App.CheckLicenses();
                    HandleActivityChanges(false);
                    await Meal.ResumeAsync();
                }
                else
                    HandleActivityChanges(false);
            }
        };

        window.Deactivated += (s, e) => // Called on Android when shutting down the app on on Windows and Android when switching apps
        {
            if (!IsRepeated("Deactivated"))
            {
                PersistAsNeeded();
                HandleActivityChanges(true);
                StoreWindowLocation(window.X, window.Y, window.Width, window.Height);
            }
        };

        window.Stopped += (s, e) => // When the user switches to another app on android or minimizes the app on Windows
        {
            IsRepeated("Stopped");
        };

        window.Resumed += (s, e) => // Called on Windows and Android when switching to an app, closely followed by Activated when it gets focus
        {
            IsRepeated("Resumed");
        };

        window.Destroying += (s, e) => // Called on windows when shutting down the app
        {
            if (!IsRepeated("Destroying"))
            {
                PersistAsNeeded();
                HandleActivityChanges(true);
                StoreWindowLocation(window.X, window.Y, window.Width, window.Height);
            }
        };

        App.Settings = new AppSettings(); // We need to replace the fake settings before we can do much, this is the first use

        // Set the App window to a sensible (phone like) size during initialization
        if (DeviceInfo.Idiom == DeviceIdiom.Desktop || DeviceInfo.Idiom == DeviceIdiom.Tablet)
        {
            Rect position = Settings.InitialPosition;
            if (position.IsEmpty)
            {
                window.Height = WindowHeight;
                window.Width = WindowWidth;
            }
            else
            {
                window.X = position.X;
                window.Y = position.Y;
                window.Height = position.Height;
                window.Width = position.Width;
            }
        }

        return window;
    }
    #endregion
    #region Android Status Bar Manipulation
    /// <summary>
    /// Sets the status bar color and appearance based on the current application theme.
    /// </summary>
    /// <remarks>This method determines whether the application is using a dark or light theme and updates the
    /// status bar accordingly. It is typically used to ensure the status bar matches the overall app appearance for
    /// better user experience.</remarks>
    [Conditional("ANDROID")]
    public static void SetStatusBar()
    {
        bool isDark = App.Current.UserAppTheme == AppTheme.Dark || (App.Current.UserAppTheme == AppTheme.Unspecified && App.Current.RequestedTheme == AppTheme.Dark);
        SetStatusBar(isDark ? Colors.Black : Colors.White, darkIcons: !isDark);
    }
    /// <summary>
    /// Sets the status bar background color and icon style for the application.
    /// </summary>
    /// <remarks>This method is effective only on Android platforms. On other platforms, calling this method
    /// has no effect.</remarks>
    /// <param name="backgroundColor">The color to apply to the status bar background. Cannot be null.</param>
    /// <param name="darkIcons">A value indicating whether to use dark icons on the status bar. Set to <see langword="true"/> for dark icons;
    /// otherwise, <see langword="false"/> for light icons.</param>
    private static void SetStatusBar(Color backgroundColor, bool? darkIcons = null)
    {
        if (backgroundColor == null)
            return;
#if ANDROID
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CommunityToolkit.Maui.Core.Platform.StatusBar.SetColor(backgroundColor);
            if (darkIcons is not null)
                CommunityToolkit.Maui.Core.Platform.StatusBar.SetStyle(darkIcons.Value ? StatusBarStyle.DarkContent : StatusBarStyle.LightContent);
        });
#endif
    }
    #endregion
    #endregion
    #region Cloud Accessibility / Connectivity
    public static void EvaluateCloudAccessible()
    {
        if (Settings is FakeAppSettings)
            return; // We cannot do anything useful yet
        bool wifiIsPresent = Connectivity.ConnectionProfiles.Contains(ConnectionProfile.WiFi);
        // Evaluate accessibility for Meals, Venues and People
        bool wifiIsNotRequiredOrIsPresent = !Settings.WiFiOnly || wifiIsPresent;
        IsCloudAccessible = Connectivity.NetworkAccess == NetworkAccess.Internet && wifiIsNotRequiredOrIsPresent;
        IsCloudAllowed = Settings.IsCloudAccessAllowed && IsCloudAccessible;

        // Evaluate accessibility for image backup and restore
        wifiIsNotRequiredOrIsPresent = !Settings.BackupImagesOnlyWiFi || wifiIsPresent;
        IsCloudImageBackupAllowed = Settings.IsCloudAccessAllowed // We can get to the cloud
            && wifiIsNotRequiredOrIsPresent; // WiFi is present if we require it
    }

    private static async void Connectivity_ConnectivityChanged(object? sender, ConnectivityChangedEventArgs? e)
    {
        EvaluateCloudAccessible();
        if (App.InitializationComplete.Task.IsCompleted && !LicenseChecked && Connectivity.NetworkAccess == NetworkAccess.Internet)
            await CheckLicenses();
    }

    public void Initialize_Connectivity() => Connectivity_ConnectivityChanged(this, null); // set initial values

    /// <summary>
    /// Can we physically reach the Internet via an acceptable interfaces, so perhaps we require WiFi, even if it is not to be used for backup.
    /// <para>The user can limit access by setting: <see cref="AppSettings.IsCloudAccessAllowed"/> and <see cref="AppSettings.WiFiOnly"/></para>
    /// <para>Various calculated results are available as related properties:</para>
    /// <list type="bullet">
    /// <item><see cref="App.IsCloudAllowed"/> - Can we reach it AND is the user allowed to use it</item>
    /// <item>See also <see cref="AppSettings.IsCloudAccessAllowed"/> and <see cref="App.IsCloudAllowed"/></item>
    /// </list>
    /// </summary>
    internal static bool IsCloudAccessible
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                HandleActivityChanges();
            }
        }
    } = false;

    /// <summary>
    /// Is the cloud accessible (<see cref="App.IsCloudAccessible"/>) and are we permitted to use it (<see cref="AppSettings.IsCloudAccessAllowed"/>).
    /// </summary>
    internal static bool IsCloudAllowed
    {
        get => !CloudAllowedSource.IsPaused;
        set
        {
            if (value != IsCloudAllowed)
                CloudAllowedSource.IsPaused = !value;
        }
    }

    /// <summary>
    /// Is the cloud accessible (<see cref="App.IsCloudAccessible"/>) and are we permitted to use it 
    /// for image backup (<see cref="AppSettings.IsCloudAccessAllowed"/>).
    /// </summary>
    internal static bool IsCloudImageBackupAllowed
    {
        get;
        set;
    } = false;

    internal static bool RecentlyUsed => DateTime.Now - Settings.LastUse < MinimumIdleTime;
    /// <summary>
    /// Handle changes in application status, either because an Internet connection comes or goes, or because
    /// the app itself is put in the background and we don't want to do anything (like backing up files)
    /// </summary>
    /// <param name="appIsPaused">Whether or not the application is paused (passed when the app is paused or (re)started</param>
    public static void HandleActivityChanges(bool? appIsPaused = null)
    {
        if (appIsPaused is not null)
            IsRunningSource.IsPaused = (bool)appIsPaused;
        IsCloudAllowed = appIsPaused != true && Settings.IsCloudAccessAllowed && IsCloudAccessible;
    }
    /// <summary>
    /// Requests to back up data to the cloud, checking various conditions like edition limitations and network access.
    /// It prompts the user for necessary permissions.
    /// </summary>
    /// <returns>Returns a the value of <see cref="IsCloudAllowed"/> a boolean indicating whether cloud access is allowed.</returns>
    public async Task<bool> RequestCloudBackup()
    {
        if (IsLimited)
            await Utilities.DisplayAlertAsync("Cloud backup Unavailable", "Cloud backup is not supported in Basic Edition");
        else if (Connectivity.NetworkAccess != NetworkAccess.Internet)
            await Utilities.DisplayAlertAsync("Internet Unavailable", "You have no Internet access");
        else if (Settings.WiFiOnly && !Connectivity.ConnectionProfiles.Contains(ConnectionProfile.WiFi))
            await Utilities.DisplayAlertAsync("WiFi Unavailable", "You specified WiFi was required in Settings but it is not available");
        else if (!IsCloudAccessible)
            await Utilities.DisplayAlertAsync("Cloud Unavailable", "Cloud access is not available");
        else
            Settings.IsCloudAccessAllowed = await Utilities.AskAsync("Cloud Backup is Off",
                "The 'Allow Cloud Backup' program setting is off. Do you want to turn it on?");
        return IsCloudAllowed;
    }
    #endregion
    #region Debug Features
    [Conditional("DEBUG")]
    public static void DebugInitialize() => AndroidDebugInitializeBaseFolderPath();

    [Conditional("ANDROID")]
    public static void AndroidDebugInitializeBaseFolderPath()
    {
        // Running on Android 10 (API 29) a Xamarin Forms app can use publicly visible files, the next block of code enables that for testing purposes
        string debugRoot = @"/storage/emulated/0/Documents";

        if (Directory.Exists(debugRoot))
            Utilities.DebugMsg("Found " + debugRoot);
        DirectoryInfo di = new(debugRoot);
        if (di.Exists)
        {
            try
            {
                FileAttributes v = di.Attributes;
                string debugDir = Path.Combine(debugRoot, BaseFolderName);
                Directory.CreateDirectory(debugDir);
                using (Stream testStream = new FileStream(Path.Combine(debugDir, "test"), FileMode.Create, FileAccess.Write))
                    File.Delete(Path.Combine(debugDir, "test"));

                string PersonPathName = Path.Combine(debugDir, Person.PersonFolderName, Person.PersonFileName);
                if (File.Exists(PersonPathName))
                    using (Stream testStream = new FileStream(PersonPathName, FileMode.Open, FileAccess.Read))
                    {
                        BaseFolderPath = debugDir;
                    } // We are allowed to use files in a folder the developer can see, so do that
            }
            // No problem if this faults, we just keep the standard BaseFolderPath, so log it and go on
            catch (UnauthorizedAccessException ex)
            {
                Utilities.DebugMsg("Unauthorized Access to " + debugRoot + " : " + ex);
            }
            catch (Exception ex)
            {
                Utilities.DebugMsg("Exception writing to " + debugRoot + " : " + ex);
            }
        }
    }
    #endregion
    #region Licensing
    internal static event EventHandler? ProEditionVerified;
    private static readonly TimeSpan mandatoryCheckPeriod = TimeSpan.FromDays(8); // If we have not checked for a subscription in this long, check it anyway
    private static DateTime NextMandatoryCheckTime { get; set; } = DateTime.MinValue;
    /// <summary>
    /// Task that performs optional pro license checks in the background. This task runs periodically to ensure that the application's
    /// licensing status is up-to-date without interrupting the user experience. In particular, if the user suspends and re-enters the
    /// application, this task will improve the odds that the licensing state is already current and no time-consuming web service
    /// calls will be necessary.
    /// </summary>
    internal static Task? PeriodicCheckForProEditionTask = null;
    internal static async Task PeriodicCheckForProEdition(CancellationToken cancellationToken)
    {
        Utilities.DebugMsg($"Enter App.PeriodicCheckForProEdition awaiting InitializationComplete");
        await InitializationComplete.Task;
        Utilities.DebugMsg($"In App.PeriodicCheckForProEdition InitializationComplete happened");
        PauseToken IsRunning = IsRunningSource.Token;
        while (!cancellationToken.IsCancellationRequested)
        {
            // Wait until we are halfway to the next mandatory check time
            var timeOfNextOptionalCheck = NextMandatoryCheckTime - mandatoryCheckPeriod / 2;
            // If we are already past that point, so wait a fixed time before checking
            if (DateTime.Now > timeOfNextOptionalCheck)
                timeOfNextOptionalCheck = DateTime.Now + mandatoryCheckPeriod / 10;

            Utilities.DebugMsg($"PeriodicCheckForProEdition: waiting {timeOfNextOptionalCheck - DateTime.Now:c} to check for license");

            // Wait until the next optional check time, but if the app is paused allow for the missing time
            while (DateTime.Now < timeOfNextOptionalCheck)
            {
                // Wait until the app is running, if it is paused we don't want to do anything
                await IsRunning.WaitWhilePausedAsync();
                await Task.Delay((int)(mandatoryCheckPeriod.TotalMilliseconds / 30), cancellationToken);
            }
            bool verified = Billing.ProPurchase is not null && await CallWs.TryVerifyPurchase(Billing.ProPurchase);
            if (verified)
            {
                // Do the update on the main thread, just in case it matters to the UI
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    NextMandatoryCheckTime = DateTime.Now + mandatoryCheckPeriod;
                });
            }
        }
        Utilities.DebugMsg($"Exit App.PeriodicCheckForProEdition");
    }
    /// <summary>
    /// Checks the licensing status of the application, including whether a professional subscription is active
    /// and whether an OCR license is available. This method also handles user notifications regarding subscription
    /// status changes and updates application settings accordingly.
    /// </summary>
    /// <param name="mandatory">Indicates whether the license check is mandatory or may be skipped if we have a recent result.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a boolean indicating whether the
    /// license check was performed but the actual results are in global state (see <see cref="App.IsLimited"/>).</returns>
    internal static async Task<bool> CheckLicenses(bool mandatory = false)
    {
        #region Validate Initial Conditions
        if (!WsUriDefined)
            return false; // Web services are disabled, perhaps this is a new build environment, do nothing at all

        bool wasLimited = App.IsLimited; // Set earlier by a full license check 

        if (!mandatory && !wasLimited && DateTime.Now < NextMandatoryCheckTime) // Don't check for a subscription expiring yet
        {
            // We have a pro subscription and we have checked recently enough, so don't check again yet.
            Utilities.DebugMsg("App.CheckLicenses early exit - no check needed yet");
            return true;
        }

        Utilities.DebugMsg("Entered App.CheckLicenses proper, no early exit was taken");

        // Ensure we have network access - this can fail with an RPC error on Windows if we're returning from searching for an image
        try
        {
            if (Connectivity.NetworkAccess != NetworkAccess.Internet && !Utilities.IsWinUI)
            {
                Utilities.DebugMsg("App.CheckLicenses early exit - no Internet");
                return false; // Nothing useful can be done
            }
        }
        catch (Exception ex)
        {
            Utilities.DebugMsg("App.CheckLicenses faulted checking Internet - no Internet");
            ex.ReportCrash();
            return false;
        }
        #endregion
        #region Try to reach the web service until the user tells us to give up
        bool WsVersionChecked = await CallWs.GetVersionAsync();
        #endregion
        #region Check the pro license
        bool FoundProSubscription = false;
        if (WsVersionChecked)
        {
            Utilities.DebugMsg("In CheckLicenses, WsVersionChecked == true");
            // Check whether the license store knows about us
            Billing.BillingStatusType billingStatus = await Billing.GetHasProSubscriptionAsync();
            LicenseChecked = true;
            switch (billingStatus)
            {
                case Billing.BillingStatusType.ok:
                    FoundProSubscription = true;
                    if (!Settings.HadProSubscription && !App.Settings.FirstUse)
                        await Utilities.ShowAppSnackBarAsync("Pro subscription check now returns a pro subscription");
                    Settings.HadProSubscription = true;
                    // As long as the App stays in memory we don't need to check for a subscription expiring more often than
                    // every few days, so set the next mandatory check time accordingly
                    NextMandatoryCheckTime = DateTime.Now + mandatoryCheckPeriod;
                    break;
                case Billing.BillingStatusType.noInternet:
                    await Utilities.DisplayAlertAsync("No Internet", "Pro subscription check failed because no Internet connection was found");
                    LicenseChecked = false;
                    break;
                case Billing.BillingStatusType.connectionFailed:
                    await Utilities.DisplayAlertAsync("No Connection", "Pro subscription check failed because it could not connect to a service, check that the Play Store is accessible");
                    break;
                case Billing.BillingStatusType.connectionFaulted:
                    await Utilities.DisplayAlertAsync("Subscription Fault", "Pro subscription check failed because of a fault, licenses are not available");
                    LicenseChecked = false;
                    break;
                case Billing.BillingStatusType.notLicensing:
                    Utilities.DebugMsg("Pro subscription check failed because licensing is not configured");
                    LicenseChecked = false;
                    break;
                case Billing.BillingStatusType.notVerified:
                    if (Settings.HadProSubscription)
                    {
                        await Utilities.DisplayAlertAsync("Verification Failed", "Pro subscription ended because it could not be verified");
                        Settings.HadProSubscription = false;
                    }
                    break;
                case Billing.BillingStatusType.notFound:
                    if (Settings.HadProSubscription)
                    {
                        await Utilities.DisplayAlertAsync("Not Found", "Pro subscription ended because there was no record of the subscription");
                        Settings.HadProSubscription = false;
                    }
                    break;
                default:
                    await Utilities.DisplayAlertAsync("Subscription Error", "Pro subscription check failed, licenses are not available");
                    break; // treat all other errors as subscription not found
            }
        }
        else
            Utilities.DebugMsg("In CheckLicenses, WsVersionChecked == false");
        #endregion
        if (LicenseChecked)
        {
            #region Notify the user as needed and check OCR license
            IsLimited = !FoundProSubscription;
            if (FoundProSubscription && Settings.FirstUse && !Settings.IsCloudAccessAllowed)
            {
                Settings.IsCloudAccessAllowed =
                    await Utilities.AskAsync("Cloud Access",
                        "Cloud storage is off by default, do you want to turn it on? If you turn it on and already " +
                        "have DivisiBill people or venue lists backed up to the cloud they will be restored automatically.",
                        "Turn it on", "Leave it off");
            }
            else if (IsLimited && !wasLimited) // Downgrade, an unusual case but not impossible
                await Utilities.DisplayAlertAsync("Removed", "The professional subscription for DivisiBill has ended");
            if (IsLimited != wasLimited) // it changed, tell anyone who cares (usually the Settings ViewModel)
            {
                ProEditionVerified?.Invoke(null, EventArgs.Empty);
                PeriodicCheckForProEditionTask ??= Task.Run(() => PeriodicCheckForProEdition(LicenseProcessCancellationTokenSource.Token));
            }
            Utilities.DebugMsg("Checking for OCR License");
            if (await Billing.GetHasOcrLicenseAsync() == 0)
                await Billing.ConsumeDepletedOcrLicense();
            #endregion
            #region Validate and (if necessary update) the AccountId
            static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s; // Local helper function to simplify expressions below

            string? storedAccountId = NullIfEmpty(App.Settings.UserKey);
            string? proAccountId = NullIfEmpty(Billing.ProPurchase?.ObfuscatedAccountId);
            string? ocrAccountId = NullIfEmpty(Billing.OcrPurchase?.ObfuscatedAccountId);

            if (proAccountId is null || ocrAccountId is null || proAccountId == ocrAccountId)
                Utilities.DebugMsg("In CheckLicenses: ProAccountId and OcrAccountId are the same or at least one is null");
            else
                Utilities.DebugMsg("In CheckLicenses: ProAccountId and OcrAccountId differ so we'll prefer the ProAccountId value");

            if (storedAccountId is null)
            {
                // Probably a clean install, so the AccountId has not been set yet, generate a token if we must, but prefer to use an existing one
                // There are some peculiar license keys which were allocated before we started using ObfuscatedAccountId, since they are used for testing
                // we handle that case here.
                Utilities.DebugMsg("In CheckLicenses: There is no stored AccountId");
                App.Settings.UserKey = proAccountId ?? ocrAccountId ?? Utilities.GenerateToken();
            }
            else
            {
                // We have a saved AccountId already but make sure it is the same as the one in the licenses because if not that indicates a different user
                // and in that case we need to use the one for the user, not the old one that we had persisted for some previous user
                Utilities.DebugMsg($"In CheckLicenses: AccountId is already set to {storedAccountId.TruncatedTo(7)}");
                string? accountIdFromAnyLicense = NullIfEmpty(proAccountId ?? ocrAccountId);
                if (accountIdFromAnyLicense is null)
                    Utilities.DebugMsg("In CheckLicenses: No AccountId available from licenses so we'll just keep the stored one");
                else if (!string.Equals(storedAccountId, accountIdFromAnyLicense))
                {
                    // They differ, so we must be a different user now
                    Utilities.DebugMsg($"In CheckLicenses: The AccountId from the licenses {accountIdFromAnyLicense.TruncatedTo(7)} overrides the stored one {storedAccountId.TruncatedTo(7)}");
                    App.Settings.UserKey = accountIdFromAnyLicense;
                }
                else if (ocrAccountId is null)
                    Utilities.DebugMsg("In CheckLicenses: The AccountId of the Pro license matches and there is no OCR AccountId");
                else if (proAccountId is null)
                    Utilities.DebugMsg("In CheckLicenses: The AccountId of the OCR license matches and there is no Pro AccountId");
                else
                    Utilities.DebugMsg("In CheckLicenses: The AccountId of the OCR license does not match, it will be ignored");
            }
            #endregion

            Utilities.DebugMsg("Exiting CheckLicenses, found Pro Subscription = " + FoundProSubscription + ", scans left = " + Billing.ScansLeft);
            return true;
        }
        else
            Utilities.DebugMsg("Exiting CheckLicenses, license check DID NOT COMPLETE");
        return false;
    }
    #endregion
    #region Navigation
    /// <summary>
    /// Shell navigation passing a relative location and a query string like "x=1&amp;y=2".
    /// The string will be converted to <see cref="ShellNavigationQueryParameters"/> so it is only passed once.
    /// </summary>
    /// <param name="location">The URI to go to, usually a <see cref="Routes"/> constant</param>
    /// <param name="navigationParameters">Query string</param>
    /// <returns>An awaitable task that's caused when navigation completes</returns>
    public static Task PushAsync(string location, string navigationParameters) =>
        PushAsync(location, UriQueryToParameters(navigationParameters));

    /// <summary>
    /// Shell navigation passing a relative location and a name/object pair parameter.
    /// The parameter is passed in ShellNavigationQueryParameters so it is only passed once.
    /// </summary>
    /// <param name="location">The URI to go to, usually a <see cref="Routes"/> constant</param>
    /// <param name="navigationParameterName">The name of the query parameter</param>
    /// <param name="navigationParameterValue">The parameter value (which may be any object)</param>
    /// <returns>An awaitable task that's caused when navigation completes</returns>
    public static Task PushAsync(string location, string navigationParameterName, object navigationParameterValue) =>
        PushAsync(location, new ShellNavigationQueryParameters() { { navigationParameterName, navigationParameterValue } });

    /// <summary>
    /// Shell navigation passing a relative location and a ShellNavigationQueryParameters as a parameter.
    /// </summary>
    /// <param name="location">The URI to go to, usually a <see cref="Routes"/> constant</param>
    /// <param name="navigationParameter">A <see cref="ShellNavigationQueryParameters"/> object</param>
    /// <returns>An awaitable task that's caused when navigation completes</returns>
    public static Task PushAsync(string location, ShellNavigationQueryParameters? navigationParameter = null) => Shell.Current is not null
        ? navigationParameter is null
            ? Shell.Current.GoToAsync(location)
            : Shell.Current.GoToAsync(location, navigationParameter)
        : Task.CompletedTask;

    /// <summary>
    /// Shell navigation passing an absolute location and a query string like "x=1&amp;y=2".
    /// The string will be converted to <see cref="ShellNavigationQueryParameters"/> so it is only passed once.
    /// </summary>
    /// <param name="location">The URI to go to, usually a <see cref="Routes"/> constant</param>
    /// <param name="navigationParameters">Query string</param>
    /// <returns>An awaitable task that's caused when navigation completes</returns>
    public static Task GoToAsync(string location, string? navigationParameters = null) => navigationParameters is null ? PushAsync("//" + location) : PushAsync("//" + location, navigationParameters);

    /// <summary>
    /// Shell navigation passing an absolute location and a name/object pair parameter.
    /// The parameter is passed in ShellNavigationQueryParameters so it is only passed once.
    /// </summary>
    /// <param name="location">The URI to go to, usually a <see cref="Routes"/> constant</param>
    /// <param name="navigationParameterName">The name of the query parameter</param>
    /// <param name="navigationParameterValue">The parameter value (which may be any object)</param>
    /// <returns>An awaitable task that's caused when navigation completes</returns>
    public static Task GoToAsync(string location, string navigationParameterName, object navigationParameterValue) =>
        PushAsync("//" + location, new ShellNavigationQueryParameters() { { navigationParameterName, navigationParameterValue } });

    /// <summary>
    /// Shell navigation passing a relative location and a ShellNavigationQueryParameters as a parameter.
    /// </summary>
    /// <param name="location">The URI to go to, usually a <see cref="Routes"/> constant</param>
    /// <param name="navigationParameter">A <see cref="ShellNavigationQueryParameters"/> object</param>
    /// <returns>An awaitable task that's caused when navigation completes</returns>
    public static Task GoToAsync(string location, ShellNavigationQueryParameters navigationParameter) =>
        PushAsync("//" + location, navigationParameter);

    /// <summary>
    /// Convert a URI query string into a <see cref="ShellNavigationQueryParameters"/> object containing the parsed parameters as string pairs (name/value).
    /// </summary>
    /// <param name="uriQuery">The URI query string to convert.</param>
    /// <returns>A <see cref="ShellNavigationQueryParameters"/> object containing the parsed parameters.</returns>
    private static ShellNavigationQueryParameters UriQueryToParameters(string uriQuery)
    {
        ShellNavigationQueryParameters parameters = [];

        if (string.IsNullOrEmpty(uriQuery))
            return parameters;

        // Remove leading '?' if present
        string query = uriQuery.TrimStart('?');

        foreach (string param in query.Split('&'))
        {
            string[] parts = param.Split('=');
            if (parts.Length != 2)
                continue;

            string key = Uri.UnescapeDataString(parts[0]);
            string value = Uri.UnescapeDataString(parts[1]);
            parameters.Add(key, value);
        }

        return parameters;
    }
    public static Task PopAsync() => Shell.Current is not null ? Shell.Current.Navigation.PopAsync() : Task.CompletedTask;

    public static async Task GoToHomeAsync() => await GoToAsync(isTutorialMode ? Routes.TutorialPage : Routes.LineItemsPage);

    public static async Task GoToRoot(int depth = 1)
    {
        if (Shell.Current is not null)
        {
            INavigation Nav = Shell.Current.Navigation;
            if (Nav.NavigationStack.Count > depth)
                await Nav.PopToRootAsync();
            else
            {
                // Just clear the stack and go to a fixed place
                while (Nav.NavigationStack.Count > 1)
                    Nav.RemovePage(Nav.NavigationStack[Nav.NavigationStack.Count - 1]);
                await App.GoToHomeAsync();
            }
        }
    }
    #endregion
    #region Location Handling
    public static int GetDistanceTo(Location? l) => MyLocation is null || l is null || MyLocation.Accuracy.GetValueOrDefault(Distances.Inaccurate) >= Distances.Inaccurate ? Distances.Inaccurate : MyLocation.GetDistanceTo(l);
    private static async Task TryGetMyLocationAsync(CancellationToken cancellationToken)
    {
        try
        {
            PermissionStatus status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            UseLocation = status == PermissionStatus.Granted; // UWP always seems to return true
            if (!UseLocation)
            {
                await InitializationComplete.Task; // let initialization complete and try again
                status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                UseLocation = status == PermissionStatus.Granted;
            }
            if (UseLocation)
                await GetMyLocationAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Utilities.ReportCrash(ex);
            return;
        }
    }
    /// <summary>
    /// Location to use instead of the calculated one for test purposes 
    /// </summary>
    public static Location? FakeLocation
    {
        get => Settings.FakeLocation;
        set
        {
            if (!Settings.FakeLocation?.IsVeryCloseTo(value) ?? false)
            {
                Settings.FakeLocation = value;
                if (value is null)
                    UseFakeLocation = false; // If we set it to null, we don't want to use a fake location
            }
        }
    }
    /// <summary>
    /// Use the fake location, settable only in a debug build and if a fake location is defined.
    /// </summary>
    public static bool UseFakeLocation
    {
        get;
        set => field = value && Utilities.IsDebug && FakeLocation is not null;
    }
    /// <summary>
    /// The AccountId most recently used by the application
    /// </summary>
    public static ISettings Settings { get; set; } = new FakeAppSettings();

    /// <summary>
    /// Set, reset, or change the fake location to a specified value
    /// Notify the user so as to allow app page switching. 
    /// </summary>
    public static async Task RefreshLocationAsync() => await GetMyLocationAsync(CancellationToken.None);
    /// <summary>
    /// If location use is permitted try and initialize App.Location from a fake one stored in app settings
    /// </summary>
    public static async Task InitializeLocationAsync()
    {
        UseFakeLocation = false;
        if (UseLocation)
            await TryGetMyLocationAsync(LocationMonitorCancellationTokenSource.Token);
        else
            MyLocation = null;
    }
    private static async Task GetMyLocationAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (UseFakeLocation)
            {
                if (Utilities.GetDistanceBetween(MyLocation, FakeLocation) > 20) // do not report small changes 
                {
                    MyLocation = FakeLocation;
                    MyLocationChanged?.Invoke(null, EventArgs.Empty);
                }
                return;
            }
            App.GpsLocation = await Geolocation.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(30)), cancellationToken);
            if (App.GpsLocation is null || (App.GpsLocation.Accuracy.GetValueOrDefault(Distances.Inaccurate) <= Distances.AccuracyLimit && App.GpsLocation.GetDistanceTo(MyLocation) > 20)) // Don't report on small changes, it's needlessly disruptive
            {
                if (MyLocation != App.GpsLocation)
                {
                    MyLocation = App.GpsLocation;
                    MyLocationChanged?.Invoke(null, EventArgs.Empty);
                }
            }
        }
        catch (FeatureNotSupportedException)
        {
            // Handle not supported on device exception
            // Just ignore this, we will not have updated MyLocation 
        }
        catch (FeatureNotEnabledException)
        {
            // Handle not enabled on device exception
            // Just ignore this, we will not have updated MyLocation 
        }
        catch (PermissionException)
        {
            // Handle permission exception
            // Just ignore this, we will not have updated MyLocation
        }
        catch (TaskCanceledException)
        {
            // Just ignore this, we will not have updated MyLocation
        }
        catch (Exception)
        {
            // We do not know what's going on so rethrow the exception
            throw;
        }
    }
    private static async Task MonitorLocationLoopAsync(CancellationToken cancellationToken)
    {
        await InitializationComplete.Task;
        if (!UseLocation)
            return;
        PauseToken IsRunning = IsRunningSource.Token;
        while (!cancellationToken.IsCancellationRequested)
        {
            await IsRunning.WaitWhilePausedAsync();
            if (!cancellationToken.IsCancellationRequested)
                try
                {
                    await GetMyLocationAsync(cancellationToken);
                    await Task.Delay(60000, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    // Just ignore this, it's the normal shutdown mechanism
                }
        }
    }
    private static readonly Counter MonitoringLocationCounter = new();
    public static async Task StartMonitoringLocation()
    {
        if (!UseLocation)
            return;
        int nestedCalls = MonitoringLocationCounter.Increment();
        if (nestedCalls == 1)
        {
            LocationMonitorCancellationTokenSource.Dispose();
            LocationMonitorCancellationTokenSource = new CancellationTokenSource();
            LocationMonitorTask = MonitorLocationLoopAsync(LocationMonitorCancellationTokenSource.Token);
        }
        else if (nestedCalls > 1)
            await GetMyLocationAsync(CancellationToken.None);
    }
    public static async Task StopMonitoringLocation()
    {
        if (!UseLocation)
            return;
        await Task.Delay(500); // brief delay just to avoid turning it off if the next page is about to turn it on
        if (MonitoringLocationCounter.Decrement() == 0)
        {
            LocationMonitorCancellationTokenSource.Cancel();
            if (LocationMonitorTask is not null)
                await LocationMonitorTask;
        }
    }

    public static event EventHandler? MyLocationChanged;
    #endregion
    #region Backup Loop
    public static void StartBackupLoop()
    {
        if (MainBackupLoopTask is null)
        {
            Utilities.DebugMsg("Main Backup Loop starting");
            MainBackupLoopTask = Task.Run(Saver.MainLoop);
            Utilities.DebugMsg("Main Backup Loop started");
        }
    }

    public static async void StopBackupLoop()
    {
        if (MainBackupLoopTask is null)
            return; // nothing to do
        using (RequestBackupLoopStop)
        using (MainBackupLoopTask)
        {
            RequestBackupLoopStop.Cancel();
            Utilities.DebugMsg("Main Backup Loop stop requested");
            try
            {
                await MainBackupLoopTask;
            }
            catch (TaskCanceledException)
            {
            }
        }
        MainBackupLoopTask = null;
        Utilities.DebugMsg("Main Backup Loop has ended");
        #endregion
    }
}