using CommunityToolkit.Maui.Extensions;
using DivisiBill.Models;
using DivisiBill.Services;
using Sentry;
using static DivisiBill.Services.Utilities;

namespace DivisiBill.Views;

public partial class SplashPage : ContentPage
{
    public SplashPage()
    {
        InitializeComponent();
        StatusMsgInvoked += LocalStatusMsg;
        Loaded += SplashPage_Loaded;
        App.ProEditionVerified += (s, e) => editionSpan.Text = App.IsLimited ? " Basic Edition" : " Pro Edition";
    }
    ~SplashPage()
    {
        // Unsubscribe from the event to prevent memory leaks
        StatusMsgInvoked -= LocalStatusMsg;
    }

    private bool initializationStarted = false;

    /// <summary>
    /// Called when the initial page is shown, either when the program is run for the first time or when it is
    /// stopped and restarted. In the restart case everything is already initialized, so there is no need to
    /// redo it all. Of course it is possible external state (like the bills stored locally or remotely, or cloud
    /// accessibility) might have changed, so we check the ones we display.
    /// </summary>
    private async void SplashPage_Loaded(object sender, EventArgs e)
    {
        base.OnAppearing();
        // reevaluate some values that may have changed
        App.Current.Initialize_Connectivity();

        // The following code takes care of the case where the user switches away from the splash page, then back.
        // This used to happen  when logging in to OneDrive, for example, because the OAUTH login used required a switch
        // to the browser to complete it and switched back to the app when it finished.
        if (initializationStarted)
        {
            // Once it has started initialization this page has nothing else to do except act as a place to show progress
            DebugMsg("In SplashPage_Loaded, initialization already started, nothing else to do.");
        }
        else
        {
            initializationStarted = true;
            DebugMsg("In SplashPage_Loaded, initialization not started, starting it now.");
            await Task.Delay(50); // Let Navigation settle down or Popup V2 will wait forever
            await InitializeApp();
            RecordMsg("In SplashPage_Loaded, navigating away from Initialization");
            if (VersionTracking.IsFirstLaunchForCurrentVersion && !VersionTracking.IsFirstLaunchEver)
            {
                // This is the first use of this version, so show the release notes
                DebugMsg($"In SplashPage_Loaded, first use of version {VersionTracking.CurrentVersion}, going to release notes page");
                await App.GoToAsync(Routes.ReleaseNotesPage);
            }
            else
            {
                // Otherwise we go to the home page
                DebugMsg("In SplashPage_Loaded, going to Home Page");
                await App.GoToHomeAsync();
            }
        }
        DebugMsg("Exit SplashPage_Loaded");
    }
    public static async Task InitializeApp()
    {
        await StatusMsgAsync("Commencing initialization, tap the icon above to pause");
        Shell.Current.Navigating += PreventPrematureNavigation;
        App.Settings ??= new AppSettings(); // allowed to be null for testing
        await InitializeUtilitiesAsync();
        if (App.SentryAllowed && App.Settings.SendCrashAsk)
        {
            var d = await Shell.Current.ShowPopupAsync<QuestionResponse>(
                new QuestionPage("Telemetry", "Do you want to report crash data anonymously to DivisiBill Support?", App.Settings.SendCrashYes),
                Utilities.GetNullPopupOptions(false));
            // It's ok to ask the questions in debug builds, but debug builds never send reports, regardless of the answer
            App.Settings.SendCrashYes = d.Result.Yes;
            App.Settings.SendCrashAsk = d.Result.Ask;
        }
        App.EvaluateCloudAccessible(); // Set initial values
        // Ask the user about using an alternate web service on a debug build that can get to the Internet
        if (Utilities.IsDebug && Connectivity.NetworkAccess == NetworkAccess.Internet)
            CallWs.SelectAlternateWs(); // Debug only
        App.HandleActivityChanges();
        // Licensing needs Internet access but should work even if backup would require WiFi 
        if (Connectivity.NetworkAccess == NetworkAccess.Internet && App.WsUriDefined)
        {
            await StatusMsgAsync("Checking for Subscriptions and Licenses");
            await App.CheckLicenses(true);
            if (!App.IsLimited)
                App.EvaluateCloudAccessible(); // Reevaluate values
        }
        else if (!App.WsUriDefined)
            await StatusMsgAsync("Skipped Check for Licenses, web services not allowed");
        else
            await StatusMsgAsync("Skipped Check for Licenses, no Internet");
        await StatusMsgAsync("Checking location");
        App.UseLocation = await HasLocationPermissionAsync();
        await App.InitializeLocationAsync();
        DebugMsg("BaseFolderPath = " + App.BaseFolderPath);
        CryptManager.PasswordSalt = App.Settings.UserKey; // may be empty
        if (CryptManager.HasStoredPassword && !CryptManager.HasStoredRsa)
        {   // We have a stored password but can't access the corresponding RSA key pair, probably the secure storage has gone away
            await StatusMsgAsync("Unable to access stored certificate");
            await Utilities.DisplayAlertAsync("Error", "Unable to access keys for stored password.\nYou will need to enter a password to enable remote backup.", "OK");
        }
        else
        {
            await StatusMsgAsync("Starting backup to remote");
            Meal.StartBackupToRemote(); // it will pause until cloud access allowed
        }
        if (App.Settings.IsCloudAccessAllowed)
        {
            App.HandleActivityChanges(false); // make sure IsCloudAccessAllowed is noticed
            if (Connectivity.NetworkAccess != NetworkAccess.Internet)
                await StatusMsgAsync("Cloud access is allowed but the Internet is not available");
            else
                await StatusMsgAsync("Cloud access is allowed");
        }
        else
            await StatusMsgAsync("Cloud access not allowed");
        // At this point we have all the cloud access we are likely to get, so subsequent code can use remote services if they are available
        await StatusMsgAsync("Awaiting People Initialization");
        await Person.InitializeAsync(App.BaseFolderPath);
        await StatusMsgAsync("Awaiting Venue Initialization");
        await Venue.InitializeAsync(App.BaseFolderPath);
        await Meal.InitializeAsync();
        await StatusMsgAsync($"Meal lists initialized, local meal count = {Meal.LocalMealList.Count}");
        // Give the interested user enough time to pause and read the messages
        for (int i = 3; i > 0; i--)
        {
            await StatusMsgAsync("Initialization completing " + i);
            await Task.Delay(1000);
        }
        await StatusMsgAsync("Initialization complete");
        await Task.Delay(1000);
        PauseBeforeMessage = false; // Just to be sure it wasn't set at the last possible second
        App.Settings.FirstUse = false;
        App.isTutorialMode = App.Settings.ShowTutorial; // This is set to true to make the home page be the tutorial, the user can change it as desired
        Shell.Current.Navigating -= PreventPrematureNavigation;
        App.InitializationComplete.SetResult(true);
        await StatusMsgAsync(string.Empty); // Clear it in case we reuse the page
    }

    /// <summary>
    /// This is used during initialization to prevent Play Store automated testing from switching to another page prematurely.
    /// It's not clear how it triggers that, but it manages. The same switch has never been observed "in the wild". Unfortunately
    /// ShowPopupAsync uses navigation so we have to allow it when that is called.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private static void PreventPrematureNavigation(object sender, ShellNavigatingEventArgs e)
    {
        if (e.CanCancel)
        {// If we can cancel the navigation, do so if it is not for a popup
            if (e.Source == ShellNavigationSource.Push && e.Target.Location.OriginalString.Contains("Popup"))
            {
                // Don't cancel if the navigation is to a popup
                RecordMsg("Splash Page: Allowed Navigation to " + e.Target.Location.OriginalString);
            }
            else if (e.Source == ShellNavigationSource.PopToRoot && e.Current.Location.OriginalString.Contains("Popup"))
            {
                // Don't cancel if the navigation is from a popup
                RecordMsg("Splash Page: Allowed Navigation from " + e.Current.Location.OriginalString);
            }
            else
            {
                string errorMessage = $"""
                    Splash Page: Navigation canceled because it was not for a popup
                    Current: {e.Current.Location.OriginalString}
                    Target: {e.Target.Location.OriginalString}
                    Source: {e.Source}

                    {Utilities.GetAppInformation()}
                    """;
                // Report this so we can figure out how often it happens
                SentrySdk.CaptureMessage(SentryEventProcessor.PrematureNavigationTitle, scope =>
                {
                    // Attach app information and comments
                    scope.AddAttachment(System.Text.Encoding.Latin1.GetBytes(errorMessage), "ErrorMsg.txt", AttachmentType.Default, "text/plain");
                });
                e.Cancel(); // Cancel the navigation
            }
        }
        else
        {
            // If we can't cancel the navigation, just log it in case there's a fault later
            RecordMsg("Splash Page: Navigation not canceled because CanCancel was false");
        }
    }

    public void LocalStatusMsg(string msg) => Dispatcher.Dispatch(() =>
    {
        if (string.IsNullOrEmpty(msg))
        {
            statusLabel.Text = string.Empty; // clear it
        }
        else
        {
            statusLabel.Text += "\n" + msg;
            statusScrollView.ScrollToAsync(statusLabel, ScrollToPosition.End, true);
        }
    });
    private async void OnStatusTapped(object sender, EventArgs e)
    {
        if (!Utilities.PauseBeforeMessage)
            await Utilities.StatusMsgAsync("*** Pausing Messages ***");
        IsPaused = !IsPaused;
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