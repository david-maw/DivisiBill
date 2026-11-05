using static DivisiBill.Services.Utilities;

namespace DivisiBill.Views;
/// <summary>
/// This page exists in order to permit the user to be taken to the help pages automatically when they first run the program.
/// Were it not for some strangeness in navigation that only shows up in the Play Store testing it would be simple, but alas
/// we must add PreventPrematureNavigation() to handle that.
/// </summary>
public partial class GettingStartedPage : ContentPage
{
    private bool GettingStartedPageCompleted = false; // Set to true when the first use of the app has been completed, so as to work around https://github.com/dotnet/maui/issues/39859
    public GettingStartedPage()
    {
        InitializeComponent();
        Shell.Current.Navigating += PreventFlicker;
        NavigatedTo += async (_, e) =>
        {
            // Called on first use only, after OnAppearing and OnNavigatedTo have been called
            DebugMsg($"Enter GettingStartedPage.NavigatedTo, using {e.NavigationType} operation from '{e.PreviousPage?.GetType()}' (GettingStartedPageCompleted = {GettingStartedPageCompleted})");
            if (GettingStartedPageCompleted)
                return; // This should never happen, but it will on Windows until https://github.com/dotnet/maui/issues/39859 is fixed
            if (App.Settings.FirstUse) // Make testing easier
            {
                // This is the first use of the app, so we show the help page and then rely on PreventPrematureNavigation to redirect the return to the splash page
                DebugMsg("In GettingStartedPage.NavigatedTo, about to invoke getting started Help Page");
                await App.PushAsync(Routes.HelpPage + "?page=GettingStarted"); // The "Page" value is case-insensitive, we used mixed case here just to satisfy the spell checker
                App.SetStatusBar(); // Set the status bar to match the theme before switching to the help page
            }
            else
            {
                // This is the normal case where we simply jump to the startup code on the Splash page
                DebugMsg("In GettingStartedPage.NavigatedTo, about to call GotoAsync to \"//SplashPage\"");
                GettingStartedPageCompleted = true; // Record that the use of this page is over, needed until https://github.com/dotnet/maui/issues/39859 is fixed
                Shell.Current.Navigating -= PreventFlicker; // We don't need to care anymore, from now on the app never returns to this page
                // No need to change the StatusBar, by design it should be identical to the one we're already using
                await App.GoToAsync(Routes.SplashPage);
            }

            DebugMsg($"Leave GettingStartedPage.NavigatedTo");
        };
    }

    ~GettingStartedPage()
    {
        Shell.Current.Navigating -= PreventFlicker; // Just in case, we don't want to leave this handler attached if the page is destroyed for some reason
    }

    /// <summary>
    /// Prevents flicker by canceling navigation back to the GettingStartedPage and redirecting to the SplashPage instead.
    /// </summary>
    /// <param name="sender">Usually the current shell instance</param>
    /// <param name="e">Information about the proposed navigation action</param>
    private async void PreventFlicker(object? sender, ShellNavigatingEventArgs e)
    {
        if (e.CanCancel)
        {   // If we can cancel the navigation, we may do so depending on the details
            string originalString = e.Current.Location.OriginalString; // Where we navigated from
            string targetString = e.Target.Location.OriginalString; // Where we propose to navigate to
            if (e.Source is ShellNavigationSource.PopToRoot or ShellNavigationSource.Pop)
            {
                // Navigating back from somewhere, we only care about navigating back to this page but unfortunately we cannot easily
                // figure out the target page (it is often ".." for example).
                if (originalString.Contains(Routes.HelpPage))
                {
                    // This is the normal path after the initial help page has been shown, because it only happens once, record it
                    RecordMsg($"In PreventPrematureNavigation, returning from help, redirect to \"//SplashPage\"");
                    // If we are navigating back to this page from the help page, we want to go directly to the splash page instead to avoid
                    // unnecessary flicker and to avoid the user having to press back again to get to the splash page.
                    Shell.Current.Navigating -= PreventFlicker; // We don't need to care anymore, from now on the app never returns to this page
                    e.Cancel(); // Cancel the navigation back to this page
                    await App.GoToAsync(Routes.SplashPage); // go to the splash page instead, just as if this were not the first use
                    return; // This is the normal path, so no need for anything else
                }
            }
        }
    }
}