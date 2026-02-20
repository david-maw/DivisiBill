namespace DivisiBill.Controls;

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

    private void AppSnackBarPage_Closed(object sender, EventArgs e) => isOpen = false;

    private async void AppSnackBarPage_Opened(object sender, EventArgs e)
    {
        isOpen = true;
        await Task.Delay(5_000);
        if (isOpen)
            await CloseAsync();
    }

    public string Text
    {
        get => field;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }
}