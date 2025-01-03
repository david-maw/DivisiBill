using DivisiBill.Models;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class MealSummaryPage : ContentPage
{
    private readonly MealSummaryViewModel viewModel;

    public MealSummaryPage()
    {
        InitializeComponent();

        viewModel = BindingContext as MealSummaryViewModel;
    }
    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        #region Nasty kludge to get the page bindings refreshed
        var temp = BindingContext;
        BindingContext = null;
        BindingContext = temp;
        #endregion
    }

    private async void OnDelItem(object sender, EventArgs e)
    {
        await viewModel.DeleteMeal();
        await Navigation.PopAsync();
    }

    private async void OnLoadItem(object sender, EventArgs e)
    {
        await viewModel.CurrentMeal.BecomeCurrentMealAsync();
        await App.GoToRoot(2);
    }
    private void OnVenueNameTapped(object sender, TappedEventArgs e)
        => Navigation.PushAsync(new VenueEditPage(Venue.SelectOrAddVenue(viewModel.VenueName, "Created from a bill")));
}
