using DivisiBill.ViewModels;

namespace DivisiBill.Views;

/// <summary>
/// LicensesPage displays subscription status and allows users to manage their subscription.
/// Shows information about the Professional Edition features and handles upgrade/subscription management.
/// </summary>
public partial class LicensesPage : ContentPage
{
    public LicensesPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        ((LicensesViewModel)BindingContext).Refresh();
    }
}