namespace DivisiBill.Views;

public class MealListByDistancePage : MealListPage
{
    // So the first time this page is entered the sort and filter will reset
    public MealListByDistancePage()
    {
        viewModel.SortOrder = ViewModels.MealListViewModel.SortOrderType.byDistance;
        viewModel.Filter = true;
    }
}