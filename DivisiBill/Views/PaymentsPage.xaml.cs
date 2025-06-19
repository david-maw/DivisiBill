namespace DivisiBill.Views;

public partial class PaymentsPage : CommunityToolkit.Maui.Views.Popup
{
    public PaymentsPage(ViewModels.PaymentsViewModel paymentsViewModel)
    {
        BindingContext = paymentsViewModel;
        InitializeComponent();
    }
}