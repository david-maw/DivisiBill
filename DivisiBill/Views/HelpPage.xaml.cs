using CommunityToolkit.Maui.Core;
using System.Windows.Input;

namespace DivisiBill.Views;

[QueryProperty(nameof(PageName), "page")]
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
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (string.IsNullOrEmpty(PageName))
            PageName = "index";
        webView.Source = new HtmlWebViewSource
        {
            Html = $@"<html>
                    <head>
                    <style>
                    html, body {{
                        color: white;
                        background-color: black;
                    }}
                    a {{color: mediumspringgreen;}}
                    </style>
                    <meta http-equiv=""Refresh"" content=""0; url='help/{PageName.ToLower()}.html'""/>
                    </head>
                    <body>
                    <center><h1>Please Wait...Preparing Help</h1></center>
                    </body>
                    </html>"
        };
    }
    public string PageName { get; set; }

    public ICommand BackCommand { get; }

    private async void OnIndexIconClicked(object sender, System.EventArgs e) => await webView.EvaluateJavaScriptAsync("gotopage('index.html#pages')");
    private void OnExitIconClicked(object sender, EventArgs e) => ReturnToApp();

    private void ReturnToApp()
    {
        Shell.Current.Navigation.PopAsync();
        // Set the status bar color and style to match the app theme
        if (OperatingSystem.IsAndroid())
        {
            Task.Yield(); // Give the PopAsync time to work
            bool isDark = App.Current.UserAppTheme == AppTheme.Dark || (App.Current.UserAppTheme == AppTheme.Unspecified && Application.Current.RequestedTheme == AppTheme.Dark);
            CommunityToolkit.Maui.Core.Platform.StatusBar.SetColor(isDark ? Colors.Black : Colors.White);
            CommunityToolkit.Maui.Core.Platform.StatusBar.SetStyle(isDark ? StatusBarStyle.LightContent : StatusBarStyle.DarkContent);
        }
    }
}