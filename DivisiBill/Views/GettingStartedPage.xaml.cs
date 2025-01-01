using static DivisiBill.Services.Utilities;

namespace DivisiBill.Views;
public partial class GettingStartedPage : ContentPage
{
    public GettingStartedPage()
    {
        InitializeComponent();
    }
    private bool helpInvoked = false;
    private int nesting = 0;
    private bool splashInvoked = false;


    protected async override void OnAppearing()
    {
        DebugMsg($"Enter GettingStartedPage.OnAppearing, helpInvoked={helpInvoked}, nesting={nesting}");
        if (nesting > 0)
        {
            DebugMsg("Leave GettingStartedPage.OnAppearing, nested call, nothing to do");
            base.OnAppearing();
            return;
        }
        nesting++;

        if (App.Settings.FirstUse && !helpInvoked) // First use of the program and help not yet shown
        {
            helpInvoked = true;
            DebugMsg("In GettingStartedPage.OnAppearing, about to invoke getting started Help Page");
            await App.PushAsync(Routes.HelpPage + "?page=gettingstarted");
        }
        else // Reopening this page after exiting from the help subsystem, or no help needed
        {
            if (!splashInvoked)
            {
#if WINDOWS
                splashInvoked = false; // Because this page is opened twice in NET8 RC2 
#else
                splashInvoked = true;
#endif
                DebugMsg("In GettingStartedPage.OnAppearing, about to call GotoAsync to Splash");
                await Task.Delay(1); // Needed to avoid crashes in Windows, see https://github.com/dotnet/maui/issues/12313
                await App.GoToAsync(Routes.SplashPage);
            }
            else
                DebugMsg("In GettingStartedPage.OnAppearing, splashInvoked already true, nothing to do");
        }
        base.OnAppearing();
        nesting--;
        DebugMsg($"Leave GettingStartedPage.OnAppearing, helpInvoked ={helpInvoked}, nesting = {nesting}");
    }
}