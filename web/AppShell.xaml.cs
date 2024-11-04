namespace web;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(HelpPage), typeof(HelpPage));
    }
    private async void OnHelpIndexClicked(object sender, EventArgs e)
    {
        Shell.Current.FlyoutIsPresented = false;
        await Shell.Current.GoToAsync($"{nameof(HelpPage)}?page=index");
    }
    private void OnHelpClicked(object sender, EventArgs e)
    {
        Shell.Current.FlyoutIsPresented = false;
        var targetType = CurrentPage.GetType();

        string TopicName;

        if (targetType == typeof(MainPage))
            TopicName = "GettingStarted"; // There's no page with this name, but help is simpler to handle as if there was
        else
            TopicName = targetType.Name;

        Shell.Current.GoToAsync($"{nameof(HelpPage)}?page={TopicName}");
    }
}
