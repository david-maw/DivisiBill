namespace web;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        MainPage = new AppShell();
    }
    protected override Window CreateWindow(IActivationState? activationState)
    {
        ArgumentNullException.ThrowIfNull(activationState);

        Window window = base.CreateWindow(activationState);

        // Set the App window to a sensible (phone like) size
        if (DeviceInfo.Idiom == DeviceIdiom.Desktop || DeviceInfo.Idiom == DeviceIdiom.Tablet)
        {
            window.Height = 600;
            window.Width = 400;
        }
        return window;
    }
}
