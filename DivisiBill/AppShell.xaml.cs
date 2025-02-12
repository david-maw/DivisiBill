using DivisiBill.Views;
using static DivisiBill.Services.Utilities;

namespace DivisiBill;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(Routes.HelpPage, typeof(HelpPage));
        Routing.RegisterRoute(Routes.PropertiesPage, typeof(PropertiesPage));
        Routing.RegisterRoute(Routes.PersonEditPage, typeof(PersonEditPage));
        Routing.RegisterRoute(Routes.ScanPage, typeof(ScanPage));
        Routing.RegisterRoute(Routes.CameraPage, typeof(CameraPage));
        Routing.RegisterRoute(Routes.ImagePage, typeof(ImagePage)); // For the tutorial page to use
        Routing.RegisterRoute(Routes.VenueListByNamePage, typeof(VenueListByNamePage));
        Routing.RegisterRoute(Routes.MealSummaryPage, typeof(MealSummaryPage));
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (FlyoutBehavior != FlyoutBehavior.Flyout) // Initially disabled so Play Store testing cannot mess with it early
        {
            await App.InitializationComplete.Task;
            FlyoutBehavior = FlyoutBehavior.Flyout;
        }
    }
    /// <summary>
    /// Called when the application generated 'back' button is pressed.
    /// TODO MAUI WORKAROUND this is not called on Android when the MAUI generated back icon is pressed, see https://github.com/dotnet/maui/issues/9095
    /// However, we have android-specific code that calls HandleBackRequest in that case 
    /// </summary>
    /// <returns>true if the request has been handled, false if the caller should handle it</returns>
    protected override bool OnBackButtonPressed() => HandleBackRequest();

    /// <summary>
    /// Only allow application exit from the main page, from others close the flyout or just go back to the main page
    /// </summary>
    /// <returns>true if the request has been handled, false if the caller should handle it</returns>
    public bool HandleBackRequest()
    {
        if (FlyoutIsPresented)
        {
            FlyoutIsPresented = false;
            return true;
        }
        else if (Navigation.NavigationStack.Count > 1 || Navigation.ModalStack.Any()) // Bottom of NavigationStack has a null entry
        {
            DebugMsg("In Shell.OnBackButtonPressed navigation branch");
            // If there is an active back button behavior, use it
            BackButtonBehavior bb = CurrentPage.GetPropertyIfSet(BackButtonBehaviorProperty, returnIfNotSet: (BackButtonBehavior)null);
            if (bb != null)
            {
                if (bb.Command is not null && bb.IsEnabled && bb.IsVisible && bb.Command.CanExecute(null))
                    bb.Command.Execute(null);
                return true; // handled, do nothing
            }
            return base.OnBackButtonPressed();
        }
        //TODO make this work better, ideally detect the default shell page automatically
        else if (CurrentPage.GetType() == typeof(Views.LineItemsPage))
            return base.OnBackButtonPressed(); // With empty navigation stacks this will simply exit
        else
        {
            _ = App.GoToHomeAsync();
            return true; // Do not exit the program
        }
    }
    private async void OnHelpIndexClicked(object sender, EventArgs e)
    {
        Shell.Current.FlyoutIsPresented = false;
        await App.PushAsync($"{Routes.HelpPage}?page=index");
    }
    private void OnHelpClicked(object sender, EventArgs e)
    {
        Shell.Current.FlyoutIsPresented = false;
        var targetType = CurrentPage.GetType();

        if (CurrentItem.Route.Equals("Information")) // This is FlyoutContent with Embedded ShellItems 
            targetType = typeof(AboutPage); // Just use the same help page for all of them
        else if (targetType.BaseType == typeof(MealListPage))
            targetType = typeof(MealListPage);
        else if (targetType.BaseType == typeof(VenueListPage))
            targetType = typeof(VenueListPage);

        string TopicName;

        if (targetType == typeof(SettingsPage) && App.IsLimited)
            TopicName = "SettingsPageBasic"; // There's no page with this name, but help is simpler to handle as if there was
        else
            TopicName = targetType.Name;

        App.PushAsync($"{Routes.HelpPage}?page={TopicName}");
    }
    private void OnExitClicked(object sender, EventArgs e) => Application.Current.CloseWindow(Application.Current.Windows[0]);
    private void PushProperties(object sender, EventArgs e) => App.PushAsync(Routes.PropertiesPage);

    private void GoToImagePageWithCamera(object sender, EventArgs e)
    {
        if (CurrentPage is ImagePage)
            App.PushAsync(Routes.CameraPage);
        else
            App.GoToAsync(Routes.ImagePage, "StartWithCamera", "true");
    }
}