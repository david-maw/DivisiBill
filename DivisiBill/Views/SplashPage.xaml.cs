using CommunityToolkit.Maui.Views;
using DivisiBill.Models;
using DivisiBill.Services;
using static DivisiBill.Services.Utilities;

namespace DivisiBill.Views;

public partial class SplashPage : ContentPage
{
    public SplashPage()
    {
        InitializeComponent();
        StatusMsgInvoked += LocalStatusMsg;
    }

    ~SplashPage()
    { StatusMsgInvoked -= LocalStatusMsg; }

    private bool initializationStarted = false;

    /// <summary>
    /// Called when the initial page is shown, either when the program is run for the first time or when it is
    /// stopped and restarted. In the restart case everything is already initialized, so there is no need to
    /// redo it all. Of course it is possible external state (like the bills stored locally or remotely, or cloud
    /// accessibility) might have changed, but that's the moral equivalent of such changes happening while the
    /// program is active so we need to handle them anyway.
    /// </summary>
    protected override async void OnAppearing()
    {
        DebugMsg("In SplashPage.OnAppearing");
        base.OnAppearing();
        App.Current.Initialize_Connectivity();

        // The following code takes care of the case where the user switches away from the splash page, then back.
        // This happens when logging in to OneDrive, for example, because the OAUTH login used requires a switch
        // to the browser to complete it and a switch back to the app when it is finished.
        if (initializationStarted)
        {
            DebugMsg("In SplashPage.OnAppearing, initialization already started, nothing to do.");
            return;
        }
        statusLabel.Text = string.Empty;
        if (App.InitializationComplete.Task.IsCompletedSuccessfully)
        {
            statusLabel.Text = string.Empty;
            await StatusMsgAsync("Initialization already completed, quick restart");
            await Meal.RestartAsync();
            await StatusMsgAsync("Quick restart completed");
            await Task.Delay(1500); // enough time to read the text
        }
        else
        {
            await StatusMsgAsync("Commencing initialization, tap the icon above to pause");
            initializationStarted = true;
            Shell.Current.Navigating += Cancel_Navigation;
            statusLabel.Text = string.Empty;
            App.Settings ??= new AppSettings(); // allowed to be null for testing
            await InitializeUtilitiesAsync();
            if (App.SentryAllowed && App.Settings.SendCrashAsk)
            {
                dynamic d = await this.ShowPopupAsync(new QuestionPage("Telemetry", "Do you want to report crash data anonymously to DivisiBill Support?", App.Settings.SendCrashYes));
                // It's ok to ask the questions in debug builds, but debug builds never send reports, regardless of the answer
                App.Settings.SendCrashYes = d.Yes;
                App.Settings.SendCrashAsk = d.Ask;
            }
            App.EvaluateCloudAccessible(); // Set initial values
            App.HandleActivityChanges();
            if (App.WsAllowed)
            {
                await StatusMsgAsync("Checking for Subscriptions and Licenses");
                await App.CheckLicenses(true);
            }
            editionSpan.Text = App.IsLimited ? " Basic Edition" : " Pro Edition";
            await StatusMsgAsync("Checking location");
            App.UseLocation = await HasLocationPermissionAsync();
            await App.InitializeLocationAsync();
            DebugMsg("BaseFolderPath = " + App.BaseFolderPath);
            await StatusMsgAsync("Starting backup to remote");
            Meal.StartBackupToRemote(); // it will pause until cloud access allowed
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
            Utilities.PauseBeforeMessage = false; // Just to be sure it wasn't set at the last possible second
            App.Settings.FirstUse = false;
            App.Settings.UseAlternateWs = false; // You have to set this again if you want it
            Shell.Current.Navigating -= Cancel_Navigation;
            App.InitializationComplete.SetResult(true);
        }
        statusLabel.Text = string.Empty; // Clear it in case we reuse the page
        App.isTutorialMode = App.Settings.ShowTutorial;
        Utilities.RecordMsg("Navigating away from Initialization");
        await App.GoToHomeAsync();
    }

    /// <summary>
    /// This is used during initialization to prevent Play Store automated testing from switching to another page prematurely.
    /// It's not clear how it triggers that, but it manages. The same switch has never been observed "in the wild".
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void Cancel_Navigation(object sender, ShellNavigatingEventArgs e)
    {
        if (e.CanCancel)
            e.Cancel();
        await StatusMsgAsync("Completed Cancel_Navigation, CanCancel = " + e.CanCancel);
    }

    public void LocalStatusMsg(string msg) => Dispatcher.Dispatch(() =>
                                                   {
                                                       statusLabel.Text += "\n" + msg;
                                                       statusScrollView.ScrollToAsync(statusLabel, ScrollToPosition.End, true);
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