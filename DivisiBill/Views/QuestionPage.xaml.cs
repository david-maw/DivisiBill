namespace DivisiBill.Views;

public partial class QuestionPage : CommunityToolkit.Maui.Views.Popup
{
    public QuestionPage(string titleParam, string textParam, bool initialYes)
    {
        InitializeComponent();
        Title = titleParam;
        Text = textParam;
        // Now initialize values from the IQuestionActions object we were passed
        Yes = initialYes;
    }

    private void Button_Clicked(object sender, System.EventArgs e)
    {
        // Pass back the values that were set in the UI
        dynamic d = new { Yes, Ask = AskAgain };
        // Close the dialog
        Close(d);
    }

    public bool Yes
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
    } = false;
    public bool AskAgain
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
    } = true; // Must start out true or we wouldn't be in this page
    public string Title
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
}