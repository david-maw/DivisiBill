using DivisiBill.Models;
using DivisiBill.Services;
using System.ComponentModel;
using System.Diagnostics;
using DivisiBill.ViewModels;
using Microsoft.Maui.Handlers;

namespace DivisiBill;

public partial class App : Application, INotifyPropertyChanged
{
    #region Global Variables and Constants
    #region Build time feature availability checks
    // Without web services we cannot do licensing or OCR
    public static readonly bool WsAllowed = !string.IsNullOrWhiteSpace(Generated.BuildInfo.DivisiBillWsUri);
    // Bing maps is only used on the Windows test version
    public static readonly bool BingMapsAllowed = !string.IsNullOrWhiteSpace(Generated.BuildInfo.DivisiBillBingMapsSecret);
    // Sentry us used in production to report problems
    public static readonly bool SentryAllowed = !string.IsNullOrWhiteSpace(Generated.BuildInfo.DivisiBillSentryDsn); 
    #endregion
    public static string MealLoadName;
    public static DateTime MealLoadTime;
    public static bool UseLocation = true;
    public static bool LicenseChecked = false;
    /// <summary>
    /// IsLimited is the inverse of whether Professional Edition has been purchased, so it is only ever set, not reset.
    /// The normal scenario is that a person uses the Basic Edition then buys the Professional Edition so at the point
    /// where they buy it they cannot have created any cloud based backups and the fact we would not attempt to recover
    /// them during initialization does not matter.
    /// 
    /// If Basic Edition is uninstalled the state (including saved Meals, Venues and People) is lost. If an instance of
    /// Professional Edition is uninstalled, Meals, Venues and People should have been backed up to the cloud and only
    /// bill images and program options will be lost.
    /// </summary>
    public static bool IsLimited = true; // Whether capabilities are limited, set in initialization
#if DEBUG
    public const string BaseFolderName = "DivisiBillDebug";
#else
    public const string BaseFolderName = "DivisiBill";
#endif
    public static readonly bool IsDebug = Utilities.IsDebug; // Not a const so as to avoid "unreachable code" warnings
    public static readonly TimeSpan MinimumIdleTime = TimeSpan.FromMinutes(90); // A changed bill younger than this is not persisted
    public static readonly TimeSpan MaximumIdleTime = TimeSpan.FromMinutes(150); // Changed bills untouched for this long are always persisted
    public static string BaseFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), BaseFolderName);
    // On Android BaseFolderPath is typically /data/user/0/com.autoplus.divisibill/files/DivisiBill, on Windows C:\users\<user>\Documents\DivisiBill
    public static string AppFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), BaseFolderName);
    // The AppFolderPath is silently mapped into something App specific, for example 
    //   ..AppData\Local\Packages\D9049CD2-5037-432D-BC7E-2E2FB39EBA1C_9zz4h110yvjzm\LocalCache\Local\DivisiBillDebug
    // The 'magic number' is the package.appxmanifest 'package family name'.
    public static Location MyLocation;
    private static Task LocationMonitorTask;
    private static CancellationTokenSource LocationMonitorCancellationTokenSource = new CancellationTokenSource();
    public static CancellationTokenSource SaveProcessCancellationTokenSource = new CancellationTokenSource();
    public static readonly TaskCompletionSource<bool> InitializationComplete = new TaskCompletionSource<bool>();
    public static readonly PauseTokenSource IsRunningSource = new PauseTokenSource();
    public static readonly PauseTokenSource CloudAllowedSource = new PauseTokenSource();
    public static ISettings Settings = null; // This (rather than direct app properties) enables fake settings to be injected for testing
    public static CancellationTokenSource RequestBackupLoopStop;
    public static Task MainBackupLoopTask;
    public static bool IsTesting = AppDomain.CurrentDomain.FriendlyName.Equals("testhost");
    public static int ScanOption = 2;
    public static bool pauseInitialization = false;
    public static bool isTutorialMode = false;
    public static Style RedLabelText = null;
    const int WindowWidth = 600;
    const int WindowHeight = 1200;
    #endregion
    #region Initialization
    public App()
    {
        InitializeComponent();
        DebugInitialize();
#if WINDOWS
        // Do not show on/off text with switch, see https://github.com/dotnet/maui/issues/6177
        SwitchHandler.Mapper.AppendToMapping("Custom", (h, v) =>
            {
                // Get rid of On/Off label beside switch, to match other platforms
                h.PlatformView.OffContent = string.Empty;
                h.PlatformView.OnContent = string.Empty;
                h.PlatformView.MinWidth = 0;
            });
#endif
        if (Resources.TryGetValue("RedLabelTextStyle", out object value) && value is Style)
            RedLabelText = (Style)value;        // Start up the location monitoring loop
        // Change all Entry controls to auto-select text
        ModifyEntry();
        // Enable connectivity monitoring
        Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;
    }
    ~App()
    {
        Connectivity.ConnectivityChanged -= Connectivity_ConnectivityChanged;
    }

    public static new App Current => (App)Application.Current;
    /// <summary>
    /// Update all Entry controls so they initially select all text when focused
    /// </summary>
    void ModifyEntry()
    {
        EntryHandler.Mapper.AppendToMapping("MyCustomization", (handler, view) =>
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
    }
    #endregion
    #region Lifecycle and window management
    static string priorWhat = "unknown";
    protected override Window CreateWindow(IActivationState activationState)
    {
        Window window = new Window(new AppShell());

        static bool IsRepeated(string what)
        {
            bool result = string.Equals(priorWhat, what);
            Utilities.DebugMsg("Main Window state = " + what + (result ? " (repeated)" : "")  + ", previously " + priorWhat);
            priorWhat = what;
            return result;
        }

        // Save off ant persistent state - this can be called multiple times without any problem
        async void PersistAsNeeded()
        {
            Utilities.DebugMsg($"In PersistAsNeeded; initialization completed = {InitializationComplete.Task.IsCompleted}");
            if (!InitializationComplete.Task.IsCompleted)
                return; // There's no knowing what state we're in, so don't do anything
            if (Settings is not null)
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
            HandleActivityChanges(true);
        }

        void StoreWindowLocation(double x, double y, double w, double h)
        {
            if (Utilities.IsUWP)
                App.Settings.InitialPosition = new Rect(x, y, w, h);
        }

        // Outer block of CreateWindow

        Utilities.DebugMsg("In CreateWindow, assigning events");
        
        App.Settings = new AppSettings();
        window.Created += (s, e) =>
        {
            if (!IsRepeated("Created"))
                HandleActivityChanges(false);
        };

        window.Activated += async (s, e) =>
        {
            if(!IsRepeated("Activated"))
            {
                Utilities.DebugMsg($"In window.Activated; initialization completed = {InitializationComplete.Task.IsCompleted}");
                if (InitializationComplete.Task.IsCompleted)
                {
                    await App.CheckLicenses();
                    HandleActivityChanges(false);
                    await Meal.ResumeAsync();
                }
            }
        };

        window.Deactivated += (s, e) =>
        {
            if (!IsRepeated("Deactivated"))
            {
                PersistAsNeeded();
                StoreWindowLocation(window.X, window.Y, window.Width, window.Height);
            }
        };

        window.Stopped += (s, e) =>
        {
            IsRepeated("Stopped");
        };

        window.Resumed += (s, e) =>
        {
            IsRepeated("Resumed");
        };

        window.Destroying += (s, e) =>
        {
            if (!IsRepeated("Destroying"))
            {
                PersistAsNeeded();
                StoreWindowLocation(window.X, window.Y, window.Width, window.Height);
            }
        };

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
    #region Cloud Accessibility / Connectivity
    public static void EvaluateCloudAccessible()
    {
        bool wifiIsNotRequiredOrIsPresent = Settings is null || !Settings.WiFiOnly || Connectivity.ConnectionProfiles.Contains(ConnectionProfile.WiFi);
        IsCloudAccessible = Connectivity.NetworkAccess == NetworkAccess.Internet && wifiIsNotRequiredOrIsPresent;
    }

    private static async void Connectivity_ConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
    {
        EvaluateCloudAccessible();
        if (App.InitializationComplete.Task.IsCompleted && !LicenseChecked && Connectivity.NetworkAccess == NetworkAccess.Internet)
            await CheckLicenses();
    }

    public void Initialize_Connectivity() => Connectivity_ConnectivityChanged(this, null); // set initial values

    private static bool isCloudAccessible = false;
    /// <summary>
    /// Can we reach the cloud via acceptable interfaces (that is perhaps not WiFi), we might still not be permitted to use it.
    /// See also Settings.IsCloudAccessAllowed and App.IsCloudAllowed
    /// </summary>
    public static bool IsCloudAccessible
    {
        get => isCloudAccessible;
        set
        {
            if (isCloudAccessible != value)
            {
                isCloudAccessible = value;
                HandleActivityChanges();
            }
        }
    }

    /// <summary>
    /// Is the cloud usable (both enabled and connected)
    /// See also Settings.IsCloudAccessAllowed and App.IsCloudAccessible
    /// </summary>
    public static bool IsCloudAllowed
    {
        get => !CloudAllowedSource.IsPaused;
        set
        {
            if (value != IsCloudAllowed)
                CloudAllowedSource.IsPaused = !value;
        }
    }

    public static bool RecentlyUsed => DateTime.Now - Settings.LastUse < MinimumIdleTime; 
    /// <summary>
    /// Handle changes in application status, either because an Internet connection comes or goes, or because
    /// the app itself is put in the background and we don't want to do anything (like backing up files)
    /// </summary>
    /// <param name="appIsPaused">Whether or not the application is paused (passed when the app is paused or (re)started</param>
    public static void HandleActivityChanges(bool? appIsPaused = null)
    {
        if (appIsPaused is not null)
            IsRunningSource.IsPaused = (bool)appIsPaused;
        if (appIsPaused == true)
            IsCloudAllowed = false;
        else
            IsCloudAllowed = Settings is not null && Settings.IsCloudAccessAllowed && IsCloudAccessible;
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
        DirectoryInfo di = new DirectoryInfo(debugRoot);
        if (di.Exists)
        {
            try
            {
                var v = di.Attributes;
                string debugDir = Path.Combine(debugRoot, BaseFolderName);
                Directory.CreateDirectory(debugDir);
                using (Stream testStream = new FileStream(Path.Combine(debugDir, "test"), FileMode.Create, FileAccess.Write))
                    File.Delete(Path.Combine(debugDir, "test"));

                string PersonPathName = Path.Combine(debugDir, Person.PersonFolderName, Person.PersonFileName);
                if (File.Exists(PersonPathName))
                    using (Stream testStream = new FileStream(PersonPathName, FileMode.Open, FileAccess.Read))
                    { BaseFolderPath = debugDir; } // We are allowed to use files in a folder the developer can see, so do that
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
    internal static event EventHandler ProEditionVerified;
    private static DateTime NextLicenseCheckTime = DateTime.MinValue;
    // Is it ok to ask the user about licenses (or failures to get them)
    private static bool AskAboutLicense = true;
    /// <summary>
    /// Check for the presence of licenses and subscriptions. This is called during startup an on entering the Settings page.
    /// </summary>
    internal static async Task CheckLicenses(bool mandatory = false)
    {
        // First check that all the required constants have values
        if (!WsAllowed)
            return;

        bool wasLimited = App.IsLimited; // This will always be false for the call during initialization but later It may change

        if (!mandatory && !wasLimited && DateTime.Now < NextLicenseCheckTime) // Don't check for a subscription expiring yet
        {
            Utilities.DebugMsg("App.CheckLicenses early exit - no check needed yet");
            return;
        }

        Utilities.DebugMsg("Entered App.CheckLicenses proper, no early exit was taken");

        NextLicenseCheckTime = DateTime.Now + TimeSpan.FromMinutes(30);

        // Ensure we have network access - this can fail with an RPC error on Windows if we're returning from searching for an image
        try
        {
            if (Connectivity.NetworkAccess != NetworkAccess.Internet)
            {
                Utilities.DebugMsg("App.CheckLicenses early exit - no Internet");
                return; // Nothing useful can be done
            }
        }
        catch (Exception ex)
        {
            Utilities.DebugMsg("App.CheckLicenses faulted checking Internet - no Internet");
            Utilities.ReportCrash(ex);
            return;
        }

        #region Check that we can reach the web service
        var WsVersionTask = CallWs.GetVersion();
        bool WsVersionChecked = false;
        await WsVersionTask.OrDelay();
        while (!WsVersionTask.IsCompleted)
        {
            AskAboutLicense = AskAboutLicense && 
                await Utilities.AskAsync("Slow Response", "DivisiBill web service check timed out, do you want to keep waiting or continue without licenses",
                    "wait", "continue"); 
            if (!AskAboutLicense) break;
            await WsVersionTask.OrDelay();
        }
        if (WsVersionTask.IsCompleted)
        {
            Utilities.DebugMsg("In CheckLicenses, WsVersionTask.IsCompleted and result = " + WsVersionTask.Result);
            if (WsVersionTask.Result == System.Net.HttpStatusCode.OK)
                WsVersionChecked = true;
            else
            {
                while (WsVersionTask.Result != System.Net.HttpStatusCode.OK)
                {
                    AskAboutLicense = AskAboutLicense && 
                        await Utilities.AskAsync("Cloud Failure", $"Could not find the cloud licensing service ({WsVersionTask.Result}), do you want to try again or continue without licenses",
                            "try again", "continue");
                    if (!AskAboutLicense) break;
                    if (WsVersionTask.IsCompleted)
                        WsVersionTask = CallWs.GetVersion();
                    await WsVersionTask.OrDelay();
                    if (WsVersionTask.IsCompleted)
                        Utilities.DebugMsg("In CheckLicenses, retried version call, returned result = " + WsVersionTask.Result);
                }
                if (WsVersionTask.IsCompleted)
                {
                    Utilities.DebugMsg("In CheckLicenses, retried version call, WsVersionTask.IsCompleted and result = " + WsVersionTask.Result);
                    if (WsVersionTask.Result == System.Net.HttpStatusCode.OK)
                        WsVersionChecked = true;
                }
                else if (App.IsDebug)
                    await Utilities.DisplayAlertAsync("GetVersion Failed", "Retry of remote call on GetVersion failed, request returned " + WsVersionTask.Result);
            }
        }
        #endregion
        #region Check the pro license
        bool FoundProSubscription = false;
        if (WsVersionChecked)
        {
            Utilities.DebugMsg("In CheckLicenses, WsVersionChecked true");
            Task<Billing.BillingStatusType> LicenseTask = Billing.GetHasProSubscriptionAsync();
            await LicenseTask.OrDelay();
            while (!LicenseTask.IsCompleted)
            {
                AskAboutLicense = AskAboutLicense && 
                    await Utilities.AskAsync("Slow Response", "Professional Edition Check Timed Out, do you want to keep waiting or continue",
                        "wait", "continue");
                if (!AskAboutLicense) break;
                await LicenseTask.OrDelay();
            }
            if (LicenseChecked = LicenseTask.IsCompleted)
            {
                switch (LicenseTask.Result)
                {
                    case Billing.BillingStatusType.ok:
                        FoundProSubscription = true;
                        if (!Settings.HadProSubscription && !App.Settings.FirstUse)
                        {
                            await Utilities.ShowAppSnackBarAsync("Subscription check now returns a pro license");
                            Settings.HadProSubscription = true;
                        }
                        break;
                    case Billing.BillingStatusType.noInternet:
                        await Utilities.DisplayAlertAsync("No Internet", "Subscription check failed because no Internet connection was found");
                        break;
                    case Billing.BillingStatusType.connectionFailed:
                        await Utilities.DisplayAlertAsync("No Connection", "Subscription check failed because it could not connect to the service, check that the Play Store is accessible");
                        break;
                    case Billing.BillingStatusType.connectionFaulted:
                        await Utilities.DisplayAlertAsync("Subscription Fault", "Subscription check failed because of a fault, licenses are not available");
                        LicenseChecked = false;
                        break;
                    case Billing.BillingStatusType.notVerified:
                        await Utilities.DisplayAlertAsync("Verification Failed", "Subscription check failed because the subscription could not be verified");
                        break;
                    case Billing.BillingStatusType.notFound:
                        if (Settings.HadProSubscription)
                        {
                            await Utilities.DisplayAlertAsync("Not Found", "Subscription check failed because there was no record of the subscription");
                            Settings.HadProSubscription = false;
                        }
                        break;
                    default:
                        await Utilities.DisplayAlertAsync("Subscription Error", "Subscription check failed");
                        break; // treat all other errors as subscription not found
                }
            }
            else
                Utilities.DebugMsg("In CheckLicenses, Pro license check did not complete, continuing without it");
        }
        else
            Utilities.DebugMsg("In CheckLicenses, WsVersionChecked false");
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
            // Nice, there's no message if we found a pro subscription because that is 
            if (IsLimited != wasLimited) // it changed, tell the viewmodel
            {
                ProEditionVerified?.Invoke(null, null);
                (Application.Current.Resources["CloudViewModel"] as CloudViewModel)?.NotifyProPurchase();
            }
            Utilities.DebugMsg("Checking for OCR License");
            if (await Billing.GetHasOcrLicenseAsync() == 0)
                await Billing.ConsumeDepletedOcrLicense();
            #endregion

            if (string.IsNullOrEmpty(App.Settings.UserKey))
            {
                // Probably a clean install, so the UserKey has not been set yet
                if (!string.IsNullOrEmpty(Billing.ProPurchase?.ObfuscatedAccountId))
                    App.Settings.UserKey = Billing.ProPurchase?.ObfuscatedAccountId;
                else if (!string.IsNullOrEmpty(Billing.OcrPurchase?.ObfuscatedAccountId))
                    App.Settings.UserKey = Billing.OcrPurchase?.ObfuscatedAccountId;
                else
                    App.Settings.UserKey = Utilities.GenerateToken();
            }
            Utilities.DebugMsg("Exiting CheckLicenses, found Pro Subscription = " + FoundProSubscription + ", scans left = " + Billing.ScansLeft);
        }
        else
            Utilities.DebugMsg("Exiting CheckLicenses, license check DID NOT COMPLETE");
    }
    #endregion
    #region Navigation
    public static Task PushAsync(string location, string navigationParameterName, string navigationParameterValue) =>
        PushAsync(location, new ShellNavigationQueryParameters() { { navigationParameterName, navigationParameterValue } });
    public static Task PushAsync(string location, ShellNavigationQueryParameters navigationParameter = null)
    {
        if (Shell.Current is not null)
        {
            if (navigationParameter is null)
                return Shell.Current.GoToAsync(location);
            else
                return Shell.Current.GoToAsync(location, navigationParameter);
        }
        return Task.CompletedTask;
    }

    public static Task GoToAsync(string location, string navigationParameterName, string navigationParameterValue) => 
        GoToAsync(location, new ShellNavigationQueryParameters() { { navigationParameterName, navigationParameterValue } });
    public static Task GoToAsync(string location, ShellNavigationQueryParameters navigationParameter = null)
    {
        if (Shell.Current is not null)
        {
            if (navigationParameter is null)
                return Shell.Current.GoToAsync("//" + location);
            else
                return Shell.Current.GoToAsync("//" + location, navigationParameter);
        }
        return Task.CompletedTask;
    }

    public static async Task GoToHomeAsync() => await GoToAsync(isTutorialMode ? Routes.TutorialPage : Routes.LineItemsPage);

    public static async Task GoToRoot(int depth = 1)
    {
        if (Shell.Current is not null)
        {
            var Nav = Shell.Current.Navigation;
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
    public int GetDistanceTo(Location l) => MyLocation is null || l is null || MyLocation.Accuracy.GetValueOrDefault(Distances.Inaccurate) >= Distances.Inaccurate ? Distances.Inaccurate : MyLocation.GetDistanceTo(l);
    private static async Task TryGetMyLocationAsync(CancellationToken cancellationToken)
    {
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
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
    /// <summary>
    /// Location to use instead of the calculated one for test purposes 
    /// </summary>
    private static Location FakeLocation
    {
        get => fakeLocation;
        set
        {
            if (fakeLocation != value)
            //Needs a better test
            {
                Settings.FakeLocation = fakeLocation = value;
            }
        }
    }
    /// <summary>
    /// Set, reset, or change the fake location to a specified value - if it is changing (as opposed to being set
    /// from null or cleared) delay before actually setting it so as to give the tester time to switch pages and see
    /// how a dynamic change is handled. It uses toast messages to notify the user so as to allow app page switching. 
    /// </summary>
    /// <param name="newFakeLocation">The new value to use</param>
    /// <returns></returns>
    public static async Task SetFakeLocation(Location newFakeLocation)
    {
        if (newFakeLocation is not null && MyLocation is not null)
        {
            await CommunityToolkit.Maui.Alerts.Toast.Make("Will set fake location in 10s").Show();
            await Task.Delay(10_000);
        }
        FakeLocation = newFakeLocation;
        await GetMyLocationAsync(CancellationToken.None);
        await CommunityToolkit.Maui.Alerts.Toast.Make("Fake location changed").Show();
    }

    private static Location fakeLocation = null;
    /// <summary>
    /// If location use is permitted try and initialize App.Location from a fake one stored in app settings
    /// </summary>
    public static async Task InitializeLocationAsync()
    {
        if (UseLocation)
        {
            if (App.IsDebug)
            {
                FakeLocation = Settings.FakeLocation;
            }
            await TryGetMyLocationAsync(LocationMonitorCancellationTokenSource.Token);
        }
    }
    private static async Task GetMyLocationAsync(CancellationToken cancellationToken)
    {
        try
        {
            Location L = FakeLocation is not null ? FakeLocation :
                await Geolocation.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(30)), cancellationToken);
            if (L?.Accuracy.GetValueOrDefault(Distances.Inaccurate) <= Distances.AccuracyLimit && L.GetDistanceTo(MyLocation) > 20) // Don't report on small changes, it's needlessly disruptive
            {
                MyLocation = L;
                MyLocationChanged?.Invoke(null, null);
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
    private static readonly Counter MonitoringLocationCounter = new Counter();
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
            await LocationMonitorTask;
        }
    }

    public static event EventHandler MyLocationChanged;
    #endregion
    #region Backup Loop
    public static void StartBackupLoop()
    {
        if (MainBackupLoopTask is null)
        {
            Utilities.DebugMsg("Main Backup Loop starting");
            RequestBackupLoopStop = new CancellationTokenSource();
            MainBackupLoopTask = Task.Run(Saver.MainLoop);
            Utilities.DebugMsg("Main Backup Loop started");
        }
    }

    public static async void StopBackupLoop()
    {
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