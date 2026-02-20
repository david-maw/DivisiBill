namespace DivisiBill.Controls;

public partial class PaymentsPage : CommunityToolkit.Maui.Views.Popup
{
    public PaymentsPage(ViewModels.PaymentsViewModel paymentsViewModel)
    {
        BindingContext = paymentsViewModel;
        InitializeComponent();
    }
    private async void OnPopupTapped(object sender, TappedEventArgs e) => await CloseAsync();
}