namespace DivisiBill.Views;

public partial class AppSnackBarPage : CommunityToolkit.Maui.Views.Popup
{
    private bool isOpen;
    public AppSnackBarPage(string parameterText)
    {
        InitializeComponent();
        Text = parameterText;
        Opened += AppSnackBarPage_Opened;
        Closed += AppSnackBarPage_Closed;
    }

    ~AppSnackBarPage()
    {
        Opened -= AppSnackBarPage_Opened;
        Closed -= AppSnackBarPage_Closed;
    }

    private void AppSnackBarPage_Closed(object sender, CommunityToolkit.Maui.Core.PopupClosedEventArgs e) => isOpen = false;

    private async void AppSnackBarPage_Opened(object sender, CommunityToolkit.Maui.Core.PopupOpenedEventArgs e)
    {
        isOpen = true;
        await Task.Delay(5000);
        if (isOpen)
            Close();
    }

    public string Text
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }
    private void OnOk(object sender, System.EventArgs e) => Close();
}