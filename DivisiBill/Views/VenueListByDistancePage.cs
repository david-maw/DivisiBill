namespace DivisiBill.Views;

public class VenueListByDistancePage : VenueListPage
{
    protected override void OnAppearing()
    {
        base.OnAppearing();
        context.VenueSortOrder = ViewModels.VenueListViewModel.VenueSortOrderType.byDistance;
    }
}