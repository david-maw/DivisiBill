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
            // Start waiting for intent triggered updates
            await (BindingContext as RestoreViewModel)?.WaitForUpdatesAsync();
        };
    }
    public async void ExitPage()
    {
            if (App.Current.IsIntentLaunch)
            {
#if ANDROID
                Platform.CurrentActivity.Finish();
#endif
            }
            else
                await Navigation.PopModalAsync();
    }
}