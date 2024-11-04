using DivisiBill.Models;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class MealSummaryPage : ContentPage
{
    readonly MealSummaryViewModel viewModel;

    public MealSummaryPage(MealSummaryViewModel viewModel)
    {
        InitializeComponent();

        BindingContext = this.viewModel = viewModel;
    }
    // Constructor without parameters used in design mode
    public MealSummaryPage()
    {
        InitializeComponent();

        var item = new MealSummary
        {
            VenueName = "Some Venue",
            CreationTime = DateTime.Now,
        };

        BindingContext = viewModel = new MealSummaryViewModel(item);
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
