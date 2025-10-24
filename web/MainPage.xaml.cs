namespace web;

public partial class MainPage : ContentPage
{
    public MainPage() => InitializeComponent();
    private async void OnHelpIndexClicked(object sender, EventArgs e)
    {
        Shell.Current.FlyoutIsPresented = false;
        await Shell.Current.GoToAsync($"{nameof(HelpPage)}?page=index");
    }
}
