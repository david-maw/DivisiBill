namespace DivisiBill.Views;

public partial class QuestionPage : CommunityToolkit.Maui.Views.Popup
{
    private readonly IQuestionDisposition questionDisposition;
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
    }
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
public interface IQuestionDisposition
{
    string Title { get; }
    string Text { get; }
    bool Yes { get; set; }
    bool AskAgain { get; set; }
}