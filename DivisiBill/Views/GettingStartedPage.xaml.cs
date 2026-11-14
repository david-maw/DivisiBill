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
                await App.PushAsync(Routes.HelpPage + $"?page=GettingStarted&returnto={Routes.SplashPage}"); // The "Page" value is case-insensitive, we used mixed case here just to satisfy the spell checker
            }
            else
            {
                // This is the normal case where we simply jump to the startup code on the Splash page
                DebugMsg("In GettingStartedPage.NavigatedTo, about to call GotoAsync to \"//SplashPage\"");
                GettingStartedPageCompleted = true; // Record that the use of this page is over, needed until https://github.com/dotnet/maui/issues/39859 is fixed
                await App.GoToAsync(Routes.SplashPage);
            }

            DebugMsg($"Leave GettingStartedPage.NavigatedTo");
        };
    }
}