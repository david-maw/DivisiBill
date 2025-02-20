namespace DivisiBill.Views;

/// <summary>
/// Popup window to check a web service call, returns true if the original call or a retry completed false 
/// if the user elected to abandon it or not retry.
/// </summary>
public partial class CheckWebPage : CommunityToolkit.Maui.Views.Popup
{
    private readonly ViewModels.CheckWebPageViewModel ViewModel;
    public CheckWebPage(Task<HttpResponseMessage> webCallTask, Func<Task<HttpResponseMessage>> webCall)
    {
        InitializeComponent();
        BindingContext = ViewModel = new ViewModels.CheckWebPageViewModel(result => Close(result), webCallTask, webCall);
        Opened += async (sender, e) => await ViewModel.WaitForConnection();
    }
}