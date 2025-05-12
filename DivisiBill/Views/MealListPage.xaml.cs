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
    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        // Android requires a little 'breathing space' or the scroll request is ignored
        if (Utilities.IsAndroid)
            await Task.Yield();
        viewModel.OnNavigatedTo();
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
    /// <summary>
    /// A method to pass into the viewModel to permit it to scroll the active collection view to a specific item.  
    /// </summary>
    /// <param name="index">The index of the item to scroll to.</param>
    /// <param name="scrollToPosition">The position in the window to position the item (Start or End usually).</param>
    /// <param name="animate">Indicates whether the scrolling should be animated.</param>
    private void ScrollItemsTo(int index, ScrollToPosition scrollToPosition, bool animate = true) // Passed in to viewModel
    {
        if (viewModel.IsGrouped)
            CurrentGroupView.ScrollTo(index, position: scrollToPosition, animate: animate);
        else
            CurrentCollectionView.ScrollTo(index, position: scrollToPosition, animate: animate);
    }
    // Ideally this would be handled in properties of the CollectionView but it isn't
    // Also beware, on Windows it is not always called
    private void OnCollectionViewScrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        //Utilities.DebugMsg($"InOnCollectionViewScrolled FirstVisibleItemIndex={e.FirstVisibleItemIndex}, LastVisibleItemIndex={e.LastVisibleItemIndex}");
        viewModel.FirstVisibleItemIndex = e.FirstVisibleItemIndex;
        viewModel.LastVisibleItemIndex = e.LastVisibleItemIndex;
    }
    #endregion
}