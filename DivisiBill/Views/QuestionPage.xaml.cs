using System.Runtime.CompilerServices;

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
    public bool Yes { get; set => SetProperty(ref field, value); } = false;
    public bool AskAgain { get; set => SetProperty(ref field, value); } = true; // Must start out true or we wouldn't be in this page
    public string Title { get; set => SetProperty(ref field, value); }
    public string Text { get; set => SetProperty(ref field, value); }
    protected bool SetProperty<T>(ref T backingStore, T value, Action onChanged = null,
    [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(backingStore, value))
            return false;
        backingStore = value;
        onChanged?.Invoke();
        OnPropertyChanged(propertyName);
        return true;
    }
}