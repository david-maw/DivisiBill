using CommunityToolkit.Maui.Core.Platform;
using DivisiBill.Controls;
using DivisiBill.ViewModels;

namespace DivisiBill.Views;

public partial class PropertiesPage : ContentPage
{
    private readonly PropertiesViewModel viewModel;
    private bool isFlyoutBehaviorOverridden;

    public PropertiesPage()
    {
        InitializeComponent();
        viewModel = (PropertiesViewModel)BindingContext;
        var amountEntries = FindVisualChildren<AmountEntry>(this);

        foreach (var entry in amountEntries.Where(en => !en.IsReadOnly))
        {
            entry.Focused += OnFocused;
            entry.Unfocused += OnInputViewUnfocused;
            entry.Completed += OnCompleted;
        }
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
        isFlyoutBehaviorOverridden = false;
        if (Shell.Current.Navigation.NavigationStack.Count > 1
            && Shell.Current.FlyoutBehavior is not FlyoutBehavior.Disabled) // we got here by push navigation
        {
            Shell.Current.FlyoutBehavior = FlyoutBehavior.Disabled;
            isFlyoutBehaviorOverridden = true;
        }
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        if (isFlyoutBehaviorOverridden)
        {
            Shell.Current.ClearValue(Shell.FlyoutBehaviorProperty); // so undo the fact we set it explicitly
            isFlyoutBehaviorOverridden = false;
        }
    }

    // Manage keyboard visibility for InputViews (Entry and Edit controls)
    InputView? currentInputView = null;
    private async void OnFocused(object? sender, FocusEventArgs e)
    {
        currentInputView = sender as InputView;
        if (currentInputView is not null)
            await currentInputView.ShowKeyboardAsync();
    }
    private async void OnInputViewUnfocused(object? sender, FocusEventArgs e)
    {
        await Task.Yield();// Give Focused for the next control a chance to run

        if (currentInputView is null && sender is InputView inputView)
            await inputView.HideKeyboardAsync();
    }
    private async void OnCompleted(object? sender, EventArgs e) => (sender as VisualElement)?.Unfocus();
    private static IEnumerable<T> FindVisualChildren<T>(Element parent) where T : Element
    {
        if (parent == null)
            yield break;

        if (parent is T match)
            yield return match;

        if (parent is IElementController controller)
        {
            foreach (var child in controller.LogicalChildren)
            {
                foreach (var descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }
    }
}