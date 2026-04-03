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

        webView.Source = new HtmlWebViewSource
        {
            Html = $"""
                    <html>
                    <head>
                      <meta name="viewport" content="width=device-width, initial-scale=1.0">
                      <title>Preparing Help</title>
                      <link rel="stylesheet" href="styles.css">
                    <meta http-equiv="Refresh" content="0; url='help/{PageName.ToLower()}.html{Fragment}'"/>
                    </head>
                    <body>
                    <center><h1>Please Wait...Preparing Help</h1></center>
                    </body>
                    </html>
                    """
        };
    }
    public string PageName { get; set; }
    public string Fragment { get; set; }

    public ICommand BackCommand { get; }

    private async void OnIndexIconClicked(object sender, System.EventArgs e) => await webView.EvaluateJavaScriptAsync("gotopage('index.html#pages')");
    private void OnExitIconClicked(object sender, EventArgs e) => ReturnToApp();

    private void ReturnToApp() => Shell.Current.Navigation.PopAsync();
}