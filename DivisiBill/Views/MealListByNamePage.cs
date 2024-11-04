namespace DivisiBill.Views;

public class MealListByNamePage : MealListPage
{
    // So the first time this page is entered the sort and filter will reset
    public MealListByNamePage()
    {
        viewModel.SortOrder = ViewModels.MealListViewModel.SortOrderType.byName;
        viewModel.Filter = true;
    }
}