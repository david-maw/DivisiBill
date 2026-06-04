using DivisiBill.Services;
using System.Windows.Input;

namespace DivisiBill.Views;

[QueryProperty(nameof(PageName), "page")]
[QueryProperty(nameof(Fragment), "fragment")]
public partial class HelpPage : ContentPage
{
    public HelpPage()
    {
        BackCommand = new Command<string>((s) =>
        {
            if (webView.CanGoBack)
                webView.GoBack();
            else
                ReturnToApp();
        });
        InitializeComponent();
    }
    protected override async void OnNavigatedTo(NavigatedToEventArgs e)
    {
        base.OnNavigatedTo(e);
#if WINDOWS
        // WebView2 doesn't automatically apply the application theme, so we have to do it ourselves
        // until https://github.com/dotnet/maui/issues/34823 is fixed.
        if (webView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 wv)
        {
            bool isDark = App.Current?.RequestedTheme == AppTheme.Dark;
            await wv.EnsureCoreWebView2Async();
            wv.CoreWebView2.Profile.PreferredColorScheme =
                isDark
                ? Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark
                : Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light;
        }
#endif
        if (string.IsNullOrEmpty(PageName))
            PageName = "index";
        if (!string.IsNullOrEmpty(Fragment))
            Fragment = "#" + Fragment.ToLower();

        // This allows us to have a virtual web site of help files embedded as resources in the app and
        // navigate between them with links in the HTML.

        // On Windows the initial page is just a redirect that shows a "Preparing Help" message while the
        // WebView loads the actual content.
        // This is necessary because the WebView can be slow to load the first page, especially on Windows,
        // and without this, users would just see a blank page for a few seconds which looks like a bug.
        webView.Source = Utilities.IsWinUI
            ? new HtmlWebViewSource
            {
                Html = $$"""
                    <html>
                    <head>
                      <meta name="viewport" content="width=device-width, initial-scale=1.0">
                      <title>Preparing Help</title>
                      <style>
                        * {
                            font-family: Arial, Helvetica, sans-serif;
                          }
                        @media (prefers-color-scheme: dark) {
                            html, body {
                                color: white;
                                background-color: black;
                            }
                        }
                        
                        @media (prefers-color-scheme: light) {
                            html, body {
                                color: black;
                                background-color: white;
                            }
                        }
                      </style>
                      <meta http-equiv="Refresh" content="0; url='help/{{PageName.ToLower()}}.html{{Fragment}}'"/>
                    </head>
                    <body>
                      <center><h1>Please Wait...</h1></center>
                      <center><h1>Preparing Help</h1></center>
                    </body>
                    </html>
                    """
            }
            : (WebViewSource)$"help/{PageName.ToLower()}.html{Fragment}"; // not Windows, probably Android, no intermediate page
    }
    public string PageName { get; set; }
    public string Fragment { get; set; }

    public ICommand BackCommand { get; }

    private async void OnIndexIconClicked(object sender, System.EventArgs e) => await webView.EvaluateJavaScriptAsync("gotopage('index.html#pages')");
    private void OnExitIconClicked(object sender, EventArgs e) => ReturnToApp();

    private void ReturnToApp() => Shell.Current.Navigation.PopAsync();
}