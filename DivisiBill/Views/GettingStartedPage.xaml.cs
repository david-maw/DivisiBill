using DivisiBill.Services;
using Sentry;
using static DivisiBill.Services.Utilities;

namespace DivisiBill.Views;
/// <summary>
/// This page exists in order to permit the user to be taken to the help pages automatically when they first run the program.
/// Were it not for some strangeness in navigation that only shows up in the Play Store testing it would be simple, but alas
/// we must add PreventPrematureNavigation() to handle that.
/// </summary>
public partial class GettingStartedPage : ContentPage
{
    public GettingStartedPage()
    {
        InitializeComponent();
        Shell.Current.Navigating += PreventPrematureNavigation;
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
                Shell.Current.Navigating -= PreventPrematureNavigation; // We don't need to care anymore, from now on the app never returns to this page
                await App.GoToAsync(Routes.SplashPage);
            }

            DebugMsg($"Leave GettingStartedPage_Loaded");
        };
    }

    /// <summary>
    /// This is used during initialization to prevent Play Store automated testing from switching to another page prematurely.
    /// It's not clear how it triggers that, but it manages to initiate a switch to LineItemsPage with a ShellNavigationSource 
    /// of ShellItemChanged, which should not be possible with Shell.FlyoutBehavior set to Disabled in XAML. The same switch has
    /// never been observed "in the wild". We permit navigation to and from the help page because that's expected, also 
    /// ShowPopupAsync uses navigation so we have to allow it when that is called (it isn't currently, but just in case).
    /// </summary>
    /// <param name="sender">Usually the current shell instance</param>
    /// <param name="e">Information about the proposed navigation action</param>
    private async void PreventPrematureNavigation(object sender, ShellNavigatingEventArgs e)
    {
        if (e.CanCancel)
        {   // If we can cancel the navigation, we may do so depending on the details
            bool navigationAllowed = true;
            string originalString = e.Current.Location.OriginalString; // Where we navigated from
            string targetString = e.Target.Location.OriginalString; // Where we propose to navigate to
            if (e.Source == ShellNavigationSource.Push)
            {
                if (originalString.Contains(Routes.GettingStartedPage)) // we only care about navigating from this page
                    navigationAllowed = (targetString.StartsWith(Routes.HelpPage) // Don't cancel if the navigation is to the Help Page
                        || targetString.Contains("Popup")); // or if it is to a popup
            }
            else if (e.Source is ShellNavigationSource.PopToRoot or ShellNavigationSource.Pop)
            {
                // Navigating back from somewhere, we only care about navigating back to this page but unfortunately we cannot easily
                // figure out the target page (it is often ".." for example).
                if (originalString.Contains(Routes.HelpPage))
                {
                    // This is the normal path after the initial help page has been shown, because it only happens once, record it
                    RecordMsg($"In PreventPrematureNavigation, returning from help, redirect to splash page");
                    // If we are navigating back to this page from the help page, we want to go directly to the splash page instead
                    Shell.Current.Navigating -= PreventPrematureNavigation; // We don't need to care anymore, from now on the app never returns to this page
                    e.Cancel(); // Cancel the navigation back to this page
                    await App.GoToAsync(Routes.SplashPage); // go to the splash page instead, just as if this were not the first use
                    return; // This is the normal path, so no need for anything else
                }
                else if (!originalString.Contains("Popup"))
                    navigationAllowed = false; // If it is not from the help page or a popup, we don't allow it
            }
            else
                navigationAllowed = false; // If it is not a Push or Pop or PopToRoot, we don't recognize it
            if (navigationAllowed)
                RecordMsg($"GettingStartedPage: Allowed {e.Source} navigation from {originalString} to {targetString}");
            else
            {
                string errorMessage = $"""
                    GettingStartedPage: Navigation canceled because it was not a permitted ShellNavigationSource orpush or pop
                    Original: {originalString}
                    Target: {targetString}
                    ShellNavigationSource: {e.Source}

                    {GetAppInformation()}
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
            RecordMsg("GettingStartedPage: Navigation not canceled because CanCancel was false");
        }
    }
}