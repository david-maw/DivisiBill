using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Extensions;
using DivisiBill.Controls;
using DivisiBill.Models;
using DivisiBill.Services;
using System.Collections.ObjectModel;
using static DivisiBill.Services.Utilities;

namespace DivisiBill.Views;

public partial class SplashPage : ContentPage
{
    public SplashPage()
    {
        InitializeComponent();
        StatusMsgInvoked += OnStatusMsg;
        NavigatedTo += SplashPage_NavigatedTo;
        App.ProEditionVerified += (s, e) => editionSpan.Text = App.IsLimited ? " Basic Edition" : " Pro Edition";
    }
    ~SplashPage()
    {
        // Unsubscribe from the event to prevent memory leaks
        StatusMsgInvoked -= OnStatusMsg;
    }

    private bool initializationStarted = false;

    /// <summary>
    /// Called when the initial page is shown, either when the program is run for the first time or when it is
    /// stopped and restarted. In the restart case everything is already initialized, so there is no need to
    /// redo it all. Of course it is possible external state (like the bills stored locally or remotely, or cloud
    /// accessibility) might have changed, so we check the ones we display.
    /// </summary>
    private async void SplashPage_NavigatedTo(object? sender, EventArgs e)
    {
        // reevaluate some values that may have changed
        App.Current.Initialize_Connectivity();

        // The following code takes care of the case where the user switches away from the splash page, then back.
        // This used to happen  when logging in to OneDrive, for example, because the OAUTH login used required a switch
        // to the browser to complete it and switched back to the app when it finished.
        if (initializationStarted)
        {
            // Once it has started initialization this page has nothing else to do except act as a place to show progress
            DebugMsg("In SplashPage_NavigatedTo, initialization already started, nothing else to do.");
        }
        else
        {
            initializationStarted = true;
            DebugMsg("In SplashPage_NavigatedTo, initialization not started, starting it now.");
            await Task.Delay(50); // Let Navigation settle down or Popup V2 will wait forever
            await InitializeApp();
            RecordMsg("In SplashPage_NavigatedTo, navigating away from Initialization");
            if (VersionTracking.IsFirstLaunchForCurrentVersion && !VersionTracking.IsFirstLaunchEver)
            {
                // This is the first use of this version, so show the release notes
                DebugMsg($"In SplashPage_NavigatedTo, first use of version {VersionTracking.CurrentVersion}, going to release notes page");
                await App.GoToAsync(Routes.ReleaseNotesPage);
            }
            else
            {
                // Otherwise we go to the home page
                DebugMsg("In SplashPage_NavigatedTo, going to Home Page");
                await App.GoToHomeAsync();
            }
        }
        DebugMsg("Exit SplashPage_NavigatedTo");
    }

    public async Task InitializeApp()
    {
        PauseAllowed = true;
        await StatusMsgAsync("Commencing initialization");
        // Start location evaluation in parallel since it can take several seconds.
        Task InitializeLocationTask = Task.CompletedTask;
        App.UseLocation = await HasLocationPermissionAsync();
        if (App.UseLocation)
        {
            Utilities.DebugMsg("Starting location detection process");
            InitializeLocationTask = App.InitializeLocationAsync(); // Fire-and-forget, it will set App.Location when it is done
        }
        else
            await StatusMsgAsync("Location permission denied, location will not be used");
        Meal.InitializeFolders();
        if (App.SentryAllowed && App.Settings.SendCrashAsk)
        {
            IPopupResult<QuestionResponse> d = await Shell.Current.ShowPopupAsync<QuestionResponse>(
                new QuestionPage("Telemetry", "Do you want to report crash data anonymously to DivisiBill Support?", App.Settings.SendCrashYes),
                Utilities.GetNullPopupOptions(false));
            // It's ok to ask the questions in debug builds, but debug builds never send reports, regardless of the answer
            App.Settings.SendCrashYes = d.Result?.Yes ?? false;
            App.Settings.SendCrashAsk = d.Result?.Ask ?? false;
        }
        App.EvaluateCloudAccessible(); // Set initial values
        App.HandleActivityChanges();
        // Licensing needs Internet access but should work even if backup would require WiFi 
        if ((Connectivity.NetworkAccess == NetworkAccess.Internet || Utilities.IsWinUI) && App.WsUriDefined)
        {
            await StatusMsgAsync("Checking for Subscriptions and Licenses");
            bool licenseCheckWorked = await App.CheckLicenses();
            if (licenseCheckWorked)
                await StatusMsgAsync($"Pro License {(Billing.ProPurchase is not null ? "Found" : "Not Found")}, OCR license {(Billing.OcrPurchase is not null ? "Found" : "Not Found")}");
            else
                await StatusMsgAsync("License check failed, cloud access may not work");
            if (!App.IsLimited)
                App.EvaluateCloudAccessible(); // Reevaluate values
        }
        else if (!App.WsUriDefined)
            await StatusMsgAsync("Skipped Check for Licenses, web services not allowed");
        else
            await StatusMsgAsync("Skipped Check for Licenses, no Internet");
        DebugMsg("BaseFolderPath = " + App.BaseFolderPath);
        CryptManager.PasswordSalt = App.Settings.UserKey; // may be empty
        if (CryptManager.HasStoredPassword && !CryptManager.HasStoredRsa)
        {   // We have a stored password but can't access the corresponding RSA key pair, perhaps the secure storage has been corrupted
            await StatusMsgAsync("There is a stored password but we cannot read all of it");
            await Utilities.DisplayAlertAsync("Error", "Unable to access keys for stored password.\nYou will need to enter a password to enable remote backup.", "OK");
        }
        else
        {
            await StatusMsgAsync("Starting backup to remote");
            try
            {
                Meal.StartBackupToRemote(); // it will pause until cloud access allowed
            }
            catch (Exception ex)
            {
                // rare, but just in case, we don't want to crash the app if the automated backup process fails, we just continue without it
                await StatusMsgAsync("Starting backup to remote faulted, continuing without automatic remote backup:" + ex.Message);
            }
        }
        if (App.Settings.IsCloudAccessAllowed)
        {
            App.HandleActivityChanges(false); // make sure IsCloudAccessAllowed is noticed
            if (Connectivity.NetworkAccess != NetworkAccess.Internet)
                await StatusMsgAsync("Cloud access is allowed but the Internet is not available");
            else
                await StatusMsgAsync("Cloud access is available");
        }
        else
            await StatusMsgAsync("Cloud access is not allowed");
        // At this point we have all the cloud access we are likely to get, so subsequent code can use remote services if they are available
        await StatusMsgAsync("Awaiting person list Initialization");
        await Person.InitializeAsync();
        await StatusMsgAsync("Awaiting venue list Initialization");
        await Venue.InitializeAsync();
        await StatusMsgAsync("Awaiting local bill list Initialization");
        await Meal.InitializeLocalMealList();
        await StatusMsgAsync($"Local bill list initialized, bill count = {Meal.LocalMealList.Count}");
        await StatusMsgAsync(" ");
        // Give the interested user enough time to pause and read the messages
        for (int i = 3; i > 0; i--)
        {
            await StatusMsgOverwriteAsync("Initialization completing " + i);
            await Task.Delay(1000);
        }
        await StatusMsgOverwriteAsync("Finishing Initialization...");
        // If we are using location, wait for discovery to complete so we can figure out the closest meal.
        // This can take several seconds, so we started it at the beginning of initialization.
        if (App.UseLocation)
            await InitializeLocationTask;
        await Meal.SelectDefaultMeal();
        PauseAllowed = false;
        await StatusMsgAsync("Initialization complete");
        await Task.Delay(500);
        PauseBeforeMessage = false; // Just to be sure it wasn't set at the last possible second
        App.Settings.FirstUse = false;
        App.isTutorialMode = App.Settings.ShowTutorial; // This is set to true to make the home page be the tutorial, the user can change it as desired
        App.InitializationComplete.SetResult(true);
        await StatusMsgAsync(string.Empty); // Clear it in case we reuse the page
    }

    public ObservableCollection<string> StatusMsgList { get; } = ["Starting DivisiBill"];

    private void OnStatusMsg(string msg, int lineCount) => Dispatcher.Dispatch(() =>
    {
        if (string.IsNullOrEmpty(msg))
        {
            StatusMsgList.Clear();
            return;
        }
        if (lineCount <= 0)
        {
            if (StatusMsgList.Count == 0)
                StatusMsgList.Add(msg);
            else
                StatusMsgList[^1] = msg;
            return;
        }

        StatusMsgList.Add(msg);

        for (int i = 1; i < lineCount; i++)
            StatusMsgList.Add(string.Empty);

        statusCollectionView.ScrollTo(StatusMsgList.Count - 1, position: ScrollToPosition.End, animate: true);
    });

    private async void OnStatusTapped(object? sender, TappedEventArgs e)
    {
        if (!PauseAllowed)
            return;
        if (!Utilities.PauseBeforeMessage)
            await Utilities.StatusMsgAsync("*** Pausing ***");
        IsPaused = !IsPaused;
    }

    public bool PauseAllowed
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }
    public bool IsPaused
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                Utilities.PauseBeforeMessage = value;
                OnPropertyChanged();
            }
        }
    } = false;
}