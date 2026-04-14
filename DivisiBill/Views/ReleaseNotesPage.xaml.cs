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
        // The release notes are stored as a single HTML resource, so we can just use it.

        webView.Source = "help/releasenotes.html";
    }
}