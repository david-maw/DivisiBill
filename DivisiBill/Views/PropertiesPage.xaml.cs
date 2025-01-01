using CommunityToolkit.Maui.Core.Platform;
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
    }
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        viewModel.UnloadProperties();
    }

    private void GoToVenuesByName(object sender, EventArgs e) => Navigation.PushAsync(new VenueListByNamePage());

    private void OnEntryFocused(object sender, FocusEventArgs e)
    {
        if (sender is Entry focusedEntry)
        {
            focusedEntry.ShowKeyboardAsync();
        }
    }

    private void OnEntryCompleted(object sender, EventArgs e)
    {
        if (sender is Entry focusedEntry)
        {
            focusedEntry.HideKeyboardAsync();
            focusedEntry.Unfocus();
        }
    }
}