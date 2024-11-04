namespace DivisiBill.Views;

public class VenueListByDistancePage : VenueListPage
{
    protected override void OnAppearing()
    {
        base.OnAppearing();
        context.SortOrder = ViewModels.VenueListViewModel.SortOrderType.byDistance;
    }
}