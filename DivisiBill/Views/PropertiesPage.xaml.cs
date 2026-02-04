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

    // Manage keyboard visibility for InputViewss (Entry and Edit controls)
    InputView currentInputView = null;
    private async void OnFocused(object sender, FocusEventArgs e)
    {
        currentInputView = sender as InputView;
        if (currentInputView is not null)
        {
            await currentInputView.ShowKeyboardAsync();

            if (sender is Entry)
            {
                // Select all text so input replaces it
                currentInputView.CursorPosition = 0;
                currentInputView.SelectionLength = currentInputView.Text?.Length ?? 0; 
            }
        }
    }
    private async void OnInputViewUnfocused(object sender, FocusEventArgs e)
    {
        await Task.Yield();// Give Focused for the next control a chance to run

        if (currentInputView is null && sender is InputView inputView)
            await inputView.HideKeyboardAsync();
    }
    private async void OnCompleted(object sender, EventArgs e) => (sender as VisualElement)?.Unfocus();
}