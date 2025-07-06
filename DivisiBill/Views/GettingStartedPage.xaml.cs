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
        Loaded += GettingStartedPage_Loaded;
    }

    private bool helpInvoked = false;
    private int nesting = 0;
    private async void GettingStartedPage_Loaded(object sender, EventArgs e)
    {
        DebugMsg($"Enter GettingStartedPage_Loaded, helpInvoked={helpInvoked}, nesting={nesting}");
        if (nesting > 0)
        {
            RecordMsg("Leave GettingStartedPage_Loaded, nested call, nothing to do");
            return;
        }
        nesting++;

        if (App.Settings.FirstUse && !helpInvoked) // First use of the program and help not yet shown
        {
            helpInvoked = true;
            DebugMsg("In GettingStartedPage_Loaded, about to invoke getting started Help Page");
            await App.PushAsync(Routes.HelpPage + "?page=gettingstarted");
        }
        else // Reopening this page after exiting from the help subsystem, or no help needed
        {
            DebugMsg("In GettingStartedPage_Loaded, about to call GotoAsync to Splash");
            await App.GoToAsync(Routes.SplashPage);
        }
        nesting--;
        DebugMsg($"Leave GettingStartedPage_Loaded, helpInvoked ={helpInvoked}, nesting = {nesting}");
    }
}