using CommunityToolkit.Maui.Core.Platform;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class PropertiesPage : ContentPage
{
    private readonly PropertiesViewModel viewModel;
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
    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        Shell.Current.FlyoutBehavior = Shell.Current.Navigation.NavigationStack.Count > 1 // we got here by navigation
            ? FlyoutBehavior.Disabled
            : FlyoutBehavior.Flyout;
    }
    private async void OnEntryFocused(object sender, FocusEventArgs e)
    {
        if (sender is Entry focusedEntry)
        {
            await focusedEntry.ShowKeyboardAsync();
        }
    }
    private async void OnEntryCompleted(object sender, EventArgs e)
    {
        if (sender is Entry focusedEntry)
        {
            await focusedEntry.HideKeyboardAsync();
            focusedEntry.Unfocus();
        }
    }
}