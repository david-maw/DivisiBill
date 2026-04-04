namespace DivisiBill.Views;

public partial class ReleaseNotesPage : ContentPage
{
    public ReleaseNotesPage() => InitializeComponent();
    protected override async void OnNavigatedTo(NavigatedToEventArgs e)
    {
        base.OnNavigatedTo(e);
        bool isDark = App.Current?.RequestedTheme == AppTheme.Dark;

#if WINDOWS
        if (webView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 wv)
        {
            await wv.EnsureCoreWebView2Async();
            wv.CoreWebView2.Profile.PreferredColorScheme =
                isDark
                ? Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark
                : Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light;
        }
#endif
        // The release notes are stored as a single embedded resource with no links, so we can just read
        // it as a stream and then convert it to a string before displaying it in the WebView. This initializes
        // faster than the technique used for the help files, but that one handles a whole virtual web site. 
        using Stream notesStream = await FileSystem.OpenAppPackageFileAsync("help/Release Notes.html");
        using StreamReader reader = new(notesStream);
        string html = await reader.ReadToEndAsync();

        webView.Source = new HtmlWebViewSource { Html = html };
    }
}