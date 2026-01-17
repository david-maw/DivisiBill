namespace DivisiBill.Views;

public partial class DataManagementPage : ContentPage
{
    public DataManagementPage() => InitializeComponent();
    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        (BindingContext as ViewModels.DataManagementViewModel)?.OnNavigatedTo();
    }
}