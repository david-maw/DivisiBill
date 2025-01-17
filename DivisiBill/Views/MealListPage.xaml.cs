using DivisiBill.Models;
using DivisiBill.Services;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class MealListPage : ContentPage
{
    protected MealListViewModel viewModel;

    public MealListPage()
    {
        InitializeComponent();
        viewModel = (MealListViewModel)BindingContext;
        viewModel.UseMealParam = UseMeal;
        viewModel.ShowDetailsParam = ShowSummary;
        viewModel.ScrollItemsTo = ScrollItemsTo;
    }

    ~MealListPage() { viewModel.ScrollItemsTo = null; }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.OnAppearing();
    }
    protected override async void OnDisappearing()
    {
        await viewModel.OnDisappearing();
        base.OnDisappearing();
    }

    private async Task ShowSummary(MealSummary ms)
    {
        if (ms is null)
            return;
        Meal m = ms.IsForCurrentMeal ? Meal.CurrentMeal : await Meal.LoadAsync(ms, true);
        var navigationParameter = new ShellNavigationQueryParameters
                {
                    { "ShowStorage", viewModel.ShowLocalMeals && viewModel.ShowRemoteMeals }
                };
        if (m is not null)
            navigationParameter.Add("Meal", m);
        else
            navigationParameter.Add("MealSummary", ms);
        await App.PushAsync(Routes.MealSummaryPage, navigationParameter);
    }
    private async Task UseMeal(MealSummary ms)
    {
        if (ms is null)
            return;
        else if (ms.IsForCurrentMeal)
            await Utilities.ShowAppSnackBarAsync("The assignment is unnecessary, this is already the current bill");
        else
        {
            Meal m = await Meal.LoadAsync(ms, true);
            if (m is null)
                await Utilities.ShowAppSnackBarAsync("Warning: Remote Access is not currently available");
            else
            {
                await m.BecomeCurrentMealAsync();
                await App.GoToHomeAsync();
            }
        }
    }
    #region Collection Scrolling
    private void ScrollItemsTo(int index, bool toEnd) // Passed in to viewModel
        => CurrentCollectionView.ScrollTo(index, position: toEnd ? ScrollToPosition.End : ScrollToPosition.Start);
    private void OnCollectionViewScrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        viewModel.FirstVisibleItemIndex = e.FirstVisibleItemIndex;
        viewModel.LastVisibleItemIndex = e.LastVisibleItemIndex;
    }
    #endregion
}