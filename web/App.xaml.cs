namespace web;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }
    protected override Window CreateWindow(IActivationState? activationState)
    {
        ArgumentNullException.ThrowIfNull(activationState);

        Window window = new(new AppShell());

        // Set the App window to a sensible (phone like) size
        if (DeviceInfo.Idiom == DeviceIdiom.Desktop || DeviceInfo.Idiom == DeviceIdiom.Tablet)
        {
            window.Height = 600;
            window.Width = 400;
        }
        return window;
    }
}
