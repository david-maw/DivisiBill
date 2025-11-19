namespace DivisiBill.Views;

public class VenueListByNamePage : VenueListPage
{
    protected override void OnAppearing()
    {
        base.OnAppearing();
        context.VenueSortOrder = ViewModels.VenueListViewModel.VenueSortOrderType.byName;
    }
}