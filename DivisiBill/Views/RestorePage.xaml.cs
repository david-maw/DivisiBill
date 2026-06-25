using DivisiBill.Services;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class RestorePage : ContentPage
{
    public RestorePage()
    {
        InitializeComponent();
        // Set the view model for this page
        BindingContext = new RestoreViewModel() { ExitPage = ExitPage };
        Loaded += async (_, _) =>
        {
            Utilities.DebugMsg("RestorePage.Loaded: Starting to wait for intent updates");
            // Start waiting for intent triggered updates
            if (BindingContext is RestoreViewModel vm)
                await vm.WaitForUpdatesAsync();
        };
    }
    public async void ExitPage()
    {
        if (App.Current.IsIntentLaunch)
        {
#if ANDROID
            Utilities.DebugMsg("RestorePage.ExitPage: Exiting using FinishAndRemoveTask");
            Platform.CurrentActivity?.FinishAndRemoveTask();
#endif
        }
        else
        {
            Utilities.DebugMsg("RestorePage.ExitPage: Exiting using PopModalAsync");
            await Navigation.PopModalAsync();
        }
    }
}