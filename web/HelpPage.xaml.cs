using System.Windows.Input;

namespace web;

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
                Shell.Current.Navigation.PopAsync();
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
    public string PageName { get; set; } = "";

    public ICommand BackCommand { get; }

    private async void OnIndexIconClicked(object sender, System.EventArgs e) => await webView.EvaluateJavaScriptAsync("gotopage('index.html#pages')");
    private void OnExitIconClicked(object sender, EventArgs e) => Shell.Current.Navigation.PopAsync();
}