namespace DivisiBill.Views;

public partial class MealSummaryPage : ContentPage
{
    public MealSummaryPage() => InitializeComponent();
    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        #region Nasty kludge to get the page bindings refreshed
        object temp = BindingContext;
        BindingContext = null;
        BindingContext = temp;
        #endregion
    }
}
