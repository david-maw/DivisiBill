namespace DivisiBill.Views;

public partial class PaymentsPage : CommunityToolkit.Maui.Views.Popup
{
    public PaymentsPage(DivisiBill.ViewModels.PaymentsViewModel paymentsViewModel)
    {
        BindingContext = paymentsViewModel;
        InitializeComponent();
    }

    private void ClosePopup(object sender, System.EventArgs e)
    {
        Close();
    }
}