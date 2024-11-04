namespace DivisiBill.Views;

public partial class QuestionPage : CommunityToolkit.Maui.Views.Popup
{
    IQuestionDisposition questionDisposition;
    public QuestionPage(IQuestionDisposition questionDisposition)
    {
        InitializeComponent();
        Title = questionDisposition.Title;
        Text = questionDisposition.Text;
        Yes = questionDisposition.Yes;
        AskAgain = questionDisposition.AskAgain;
        this.questionDisposition = questionDisposition;
    }

    private void Button_Clicked(object sender, System.EventArgs e)
    {
        questionDisposition.Yes = Yes;
        questionDisposition.AskAgain = AskAgain;
        Close(questionDisposition);
    }

    // Bound items

    private bool yes = false;
    private bool askAgain;
    public bool Yes
    {
        get => yes;
        set
        {
            if (yes != value)
            {
                yes = value;
                OnPropertyChanged();
            }
        }
    }
    public bool AskAgain
    {
        get => askAgain;
        set
        {
            if (askAgain != value)
            {
                askAgain = value;
                OnPropertyChanged();
            }
        }
    }
    
    private string title;
    public string Title
    {
        get => title;
        set
        {
            if (title != value)
            {
                title = value;
                OnPropertyChanged();
            }
        }
    }

    private string text;
    public string Text
    {
        get => text;
        set
        {
            if (text != value)
            {
                text = value;
                OnPropertyChanged();
            }
        }
    }
}
public interface IQuestionDisposition
{
    string Title { get; }
    string Text { get; }
    bool Yes { get; set; }
    bool AskAgain { get; set; }
}