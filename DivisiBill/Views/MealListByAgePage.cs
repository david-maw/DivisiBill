namespace DivisiBill.Views;

public class MealListByAgePage : MealListPage
{
    // So the first time this page is entered the sort and filter will reset
    public MealListByAgePage()
    {
        viewModel.SortOrder = ViewModels.MealListViewModel.SortOrderType.byDate;
        viewModel.Filter =false;
    }
}