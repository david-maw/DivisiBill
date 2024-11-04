using DivisiBill.Models;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class PropertiesPage : ContentPage
{
    private PropertiesViewModel viewModel;
    public PropertiesPage()
    {
        InitializeComponent();
        viewModel = (PropertiesViewModel)BindingContext;
    }
    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Force an update of the relative time displays
        viewModel.LoadProperties();
        viewModel.CurrentMeal_PropertyChanged(null, new System.ComponentModel.PropertyChangedEventArgs(nameof(MealViewModel.ApproximateAge)));
    }
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        viewModel.UnloadProperties();
    }

    private void GoToVenuesByName(object sender, EventArgs e) => Navigation.PushAsync(new VenueListByNamePage());
}