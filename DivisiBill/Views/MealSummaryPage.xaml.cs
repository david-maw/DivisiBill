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

    private void OnVenueNameTapped(object sender, TappedEventArgs e)
        => Navigation.PushAsync(new VenueEditPage(Venue.SelectOrAddVenue(viewModel.VenueName, $"Created from bill {viewModel.Id} on {DateTime.Now:d}")));
}
