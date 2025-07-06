using static DivisiBill.Services.Utilities;

namespace DivisiBill.Views;
/// <summary>
/// This page exists in order to permit the user to be taken to the help pages automatically when they first run the program.
/// </summary>
public partial class GettingStartedPage : ContentPage
{
    public GettingStartedPage()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            DebugMsg($"Enter GettingStartedPage_Loaded");

            if (App.Settings.FirstUse)
            {
                DebugMsg("In GettingStartedPage_Loaded, about to invoke getting started Help Page");
                await App.PushAsync(Routes.HelpPage + "?page=gettingstarted");
            }
            else
            {
                DebugMsg("In GettingStartedPage_Loaded, about to call GotoAsync to Splash");
                await App.GoToAsync(Routes.SplashPage);
            }

            DebugMsg($"Leave GettingStartedPage_Loaded");
        };
        Shell.Current.Navigating += Current_Navigating;
    }

    private async void Current_Navigating(object sender, ShellNavigatingEventArgs e)
    {
        if (e.Source == ShellNavigationSource.PopToRoot)
        {
            DebugMsg($"In GettingStartedPage_Navigating, returning from help, redirect to splash page");
            // If we are navigating to the root page, we want to go to the splash page, not the GettingStartedPage
            Shell.Current.Navigating -= Current_Navigating; // We don't need to care anymore, from now on the app never returns to this page
            e.Cancel(); // Cancel the navigation back to this page
            await App.GoToAsync(Routes.SplashPage); // go to the splash page instead, just as if this were not the first use
            return;
        }
    }
}