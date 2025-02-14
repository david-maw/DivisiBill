using System.Net;

namespace DivisiBill.Views;

public partial class CheckWebPage : CommunityToolkit.Maui.Views.Popup
{
    private readonly ViewModels.CheckWebPageViewModel ViewModel;
    public CheckWebPage(Task<HttpStatusCode> WsVersionTaskParam)
    {
        InitializeComponent();
        BindingContext = ViewModel = new ViewModels.CheckWebPageViewModel(WsVersionTaskParam, (object result) => Close(result));
        Opened += async (sender, e) => await ViewModel.WaitForConnection();
    }
}