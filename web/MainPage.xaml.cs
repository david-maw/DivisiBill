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
        await Shell.Current.GoToAsync($"{nameof(HelpPage)}?page=index");
    }
}
