namespace web;

public partial class MainPage : ContentPage
{
    public MainPage() => InitializeComponent();
    private async void OnHelpIndexClicked(object sender, EventArgs e)
    {
#if WINDOWS
        try
        {
            var installedHelpFolder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.Combine(AppContext.BaseDirectory, "help"));
            var files = await installedHelpFolder.GetFilesAsync();

            System.Diagnostics.Debug.WriteLine($"Enumerating ({files?.Count ?? 0}) help files");
            if ( files is not null )
                foreach (var file in files)
                {
                    System.Diagnostics.Debug.WriteLine(file.Path);
                }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error enumerating help files: {ex.Message}");
        }
#endif
        Shell.Current.FlyoutIsPresented = false;
        await Shell.Current.GoToAsync($"{nameof(HelpPage)}?page=index&fragment=pages");
    }
    public bool Dark
    {
        set
        {
            if (value != Dark)
            {
                Application.Current?.UserAppTheme = value ? AppTheme.Dark : AppTheme.Light;
            }
        }
        get => Application.Current?.UserAppTheme == AppTheme.Dark || Application.Current?.RequestedTheme == AppTheme.Dark;
    }
    private async void OnGettingStarted(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(HelpPage) + "?page=GettingStarted"); // The "Page" value is case-insensitive, we used mixed case here just to satisfy the spell checker
    }
    private async void OnLineItems(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(HelpPage) + "?page=LineItemsPage"); // The "Page" value is case-insensitive, we used mixed case here just to satisfy the spell checker
    }
}
