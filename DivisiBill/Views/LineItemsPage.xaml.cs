using CommunityToolkit.Maui.Core.Platform;
using DivisiBill.Controls;
using DivisiBill.Models;
using DivisiBill.Services;
using DivisiBill.ViewModels;
using System.Diagnostics;

namespace DivisiBill.Views;

public partial class LineItemsPage : ContentPage, IQueryAttributable
{
    private MealViewModel mealViewModel = new(); // Dummy value to avoid null reference warnings
    private readonly SharerButton[] ShareButtons;
    public LineItemsPage()
    {
        InitializeComponent();
        SizeChanged += LineItemsPage_SizeChanged;
        // Initialize the shares buttons
        ShareButtons = [.. SharesContainer.Children.Select(v => (SharerButton)v)];
    }

    public string Command { get; set; } = string.Empty;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("command", out object? commandValue))
            Command = Uri.UnescapeDataString(commandValue?.ToString() ?? string.Empty);
    }

    private void LineItemsPage_SizeChanged(object? sender, EventArgs e)
    {
        if (mealViewModel is not null)
            try
            {
                ArrangeSharesButtons();
                var li = (LineItem)LineItemsListView.SelectedItem;
                if (li is not null)
                {
                    DrawAllSharesButtons(li);
                    LineItemsListView.ScrollTo(li);
                }
            }
#pragma warning disable IDE0059 // Unnecessary assignment of a value
#pragma warning disable CS0168 // Unnecessary assignment of a value
            catch (Exception ex)
#pragma warning restore CS0168 // Unnecessary assignment of a value
#pragma warning restore IDE0059 // Unnecessary assignment of a value
            {
                if (Utilities.IsDebug)
                    Debugger.Break();
                // Do nothing in a release build, the layout might be wrong, but that's all
            }
    }
    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        if (BindingContext is not MealViewModel vm)
            throw new InvalidOperationException("The BindingContext of LineItemsPage must be set to a MealViewModel before the page appears");
        mealViewModel = vm;
        mealViewModel.ShowLineItemsHint = App.Settings is not null && App.Settings.ShowLineItemsHint;
        mealViewModel.LineItemAddCompletedInUi = LineItemAddCompletedInUi;
        ArrangeSharesButtons(); // In case the sharer list changed
        if (Command is not null && Command.Equals("SelectFirstUnallocatedLineItem"))
        {
            SelectFirstUnallocatedLineItem();
            Command = string.Empty; // Clear the command so we don't do it again if the page is reloaded
        }
        var li = (LineItem)LineItemsListView.SelectedItem;
        if (li is not null)
            DrawAllSharesButtons(li);
        mealViewModel.SharingChanged += MealViewModel_SharingChanged;
        mealViewModel.SharesButtonsChanged += MealViewModel_SharesButtonsChanged;
        mealViewModel.ScrollLineItemsTo = ScrollLineItemsTo;
    }
    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        mealViewModel.SharingChanged -= MealViewModel_SharingChanged;
        base.OnNavigatedFrom(args);
        mealViewModel.SharesButtonsChanged -= MealViewModel_SharesButtonsChanged;
        Meal.RequestSnapshot();
        mealViewModel.ForgetDeletedItems();
        mealViewModel.ScrollLineItemsTo = (_, _) => { };
    }
    private void MealViewModel_SharingChanged(LineItem li)
    {
        if (li == (LineItem)LineItemsListView.SelectedItem)
        {
            mealViewModel.IsSharesCountVisible = false;
            DrawAllSharesButtons(li);
        }
    }

    private void MealViewModel_SharesButtonsChanged(LineItem.DinerID _)
    {
        if (mealViewModel.SelectedLineItem is not null)
            DrawAllSharesButtons(mealViewModel.SelectedLineItem);
    }

    /// <summary>
    /// Set the correct width for a button corresponding to each sharer (each PersonCost item in Costs)
    /// The shape of each button has meaning and is set separately in DrawSharesButtons 
    /// </summary>
    private void ArrangeSharesButtons()
    {
        if (Width <= 0) // We do not yet know the size, so just give it up
            return;
        try
        {
            // Figure out how wide each visible button can be
            int buttons = mealViewModel.Costs.Count;
            double totalAvailableForButtons = Width - SharesContainer.Margin.Left - SharesContainer.Spacing * (buttons - 1) - SharesContainer.Margin.Right;
            double buttonWidth = totalAvailableForButtons / buttons; // this will be integer arithmetic so there could be a bit left over
            // First hide all of them
            foreach (var button in ShareButtons)
                button.IsVisible = false;
            // Now set up and show a button for each PersonCost
            foreach (PersonCost personCost in mealViewModel.Costs)
            {
                var button = ShareButtons[personCost.DinerIndex];
                button.IsVisible = true;
                button.CommandParameter = personCost;
                button.LongPressCommandParameter = personCost;
                button.Text = personCost.Nickname;
                button.WidthRequest = buttonWidth;
            }
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            return; // The button layout may be messed up, but the program will run just fine
        }
    }
    private static void DrawSharesButton(SharerButton btn, byte? shares)
    {
        btn.BorderWidth = shares > 0 ? 2 : 0;
        btn.CornerRadius = shares > 1 ? 20 : 0;
    }

    private void DrawAllSharesButtons(LineItem li)
    {
        if (li is null || ShareButtons is null)
            return;
        foreach (var btn in ShareButtons)
        {
            if (btn.IsVisible && btn.CommandParameter is PersonCost pc)
                DrawSharesButton(btn, li.GetShares(pc.DinerID));
        }
    }

    private void SelectLineItemInUi(LineItem li) => LineItemsListView.SelectedItem = li; // Because simply setting the 'SelectedItem' property in the ViewModel does not seem to work

    private void LineItemAddCompletedInUi(LineItem li)
    {
        SelectedAmountEntry.Unfocus();
        SelectedAmountEntry.Focus();
    }

    /// <summary>
    /// Handle the selection or deselection of a new LineItem in the list, ensure that any updated values for the previous item are persisted.
    /// BEWARE because of bug https://github.com/dotnet/maui/issues/5446 this may be called before or after <see cref="MealViewModel.ToggleSelectLineItem(LineItem)"/>
    /// It is currently called after on Android, before on Windows
    /// </summary>
    /// <param name="sender">The item list</param>
    /// <param name="e">Information about what changed</param>
    private async void OnItemSelected(object? sender, SelectionChangedEventArgs e)
    {
        LineItem? priorLi = e.PreviousSelection.Count == 0 ? null : (LineItem)e.PreviousSelection[0];
        if (priorLi is not null)
        {
            // Hide the prior item if it should no longer be visible
            mealViewModel.LineItemDeselected(priorLi);
        }

        LineItem? li = e.CurrentSelection.Count == 0 ? null : (LineItem)e.CurrentSelection[0];
        ItemEntryContainer.IsVisible = li is not null;
        totalsContainer.IsVisible = li is null;
        mealViewModel.IsSharesCountVisible = false;
        SharesContainer.IsVisible = li is not null;
        if (li is null)
            await SelectedNameEntry.HideKeyboardAsync(); // This is for the case where the user does not use the enter key but just deselects the current item
        else
        {
            DrawAllSharesButtons(li);
            LineItemsListView.ScrollTo(li);
            if (SelectedAmountEntry.IsSoftInputShowing() && SelectedAmountEntry.IsFocused)
            {
                SelectedAmountEntry.Unfocus();
                SelectedAmountEntry.Focus();
            }
        }
    }

    public void SelectFirstUnallocatedLineItem()
    {
        mealViewModel.ClearFiltering();
        LineItem? li = mealViewModel.LineItems.FirstOrDefault(li2 => li2.TotalSharers == 0);
        if (li is not null)
            LineItemsListView.SelectedItem = li;
    }
    private void OnVenueNameTapped(object? sender, TappedEventArgs e) => App.PushAsync(Routes.PropertiesPage);

    #region Collection Scrolling
    private void ScrollLineItemsTo(int index, bool toEnd) // Passed in to viewModel
    => LineItemsListView.ScrollTo(index, position: toEnd ? ScrollToPosition.End : ScrollToPosition.Start);
    private void OnCollectionViewScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        mealViewModel.FirstVisibleItemIndex = e.FirstVisibleItemIndex;
        mealViewModel.LastVisibleItemIndex = e.LastVisibleItemIndex;
    }
    #endregion

    private void OnDrop(object? sender, DropEventArgs e)
    {
        e.Handled = true; // Inhibit MAUI default processing of the drop, see https://github.com/dotnet/maui/issues/35599
    }
}
