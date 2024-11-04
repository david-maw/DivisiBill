namespace DivisiBill.Views;

public class VenueListByNamePage : VenueListPage
{
    protected override void OnAppearing()
    {
        base.OnAppearing();
        context.SortOrder = ViewModels.VenueListViewModel.SortOrderType.byName;
    }
}