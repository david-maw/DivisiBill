using CommunityToolkit.Maui.Core.Platform;
using DivisiBill.Models;
using DivisiBill.Services;
using DivisiBill.ViewModels;
using System.Diagnostics;

namespace DivisiBill.Views;

[QueryProperty(nameof(Command), "command")]
public partial class LineItemsPage : ContentPage
{
    private MealViewModel mealViewModel;
    private readonly Button[] ShareButtons;
    public LineItemsPage()
    {
        InitializeComponent();
        SizeChanged += LineItemsPage_SizeChanged;
        // Initialize the shares buttons
        ShareButtons = [.. SharesContainer.Children.Select(v => (Button)v)];
    }

    private PersonCost CurrentPersonCost = null;

    public string Command { get; set; } = null;

    /// <summary>
    /// Redraw the button only after it is released in order not to create a flash on the fake long press
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnSharesBtnReleased(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is PersonCost pc && LineItemsListView.SelectedItem is LineItem li)
        {
            byte shares = li.GetShares(pc.DinerID);
            DrawSharesButton(btn, shares);
        }
    }

    /// <summary>
    /// This contains a crude but effective 'press and hold' implementation, it's subject to some problems
    /// but good enough for practical use.
    /// </summary>
    private async void OnSharesBtnPressed(object sender, EventArgs e)
    {
        if (sender is Button btn && LineItemsListView.SelectedItem is LineItem li)
        {
            CurrentPersonCost = (PersonCost)btn.CommandParameter;
            var sharer = ((PersonCost)btn.CommandParameter).DinerID;
            byte shares = CurrentShares = li.GetShares(sharer);
            if (!SharesCountContainer.IsVisible)
            {
                shares = shares == 0 ? (byte)1 : (byte)0;
                li.SetShares(sharer, shares);
            }
            UpdateSharesInfoHeaderText(CurrentShares);
            // Now simulate long press
            CurrentSharesButton = btn;
            await Task.Delay(500);
            if (btn == CurrentSharesButton && btn.IsPressed) // is the same button currently pressed
            {
                li.SetShares(sharerID: CurrentPersonCost.DinerID, CurrentShares);
                DrawSharesButton(CurrentSharesButton, CurrentShares);
                SharesCountContainer.IsVisible = true;
            }
        }
    }
    private void LineItemsPage_SizeChanged(object sender, EventArgs e)
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
    protected override void OnAppearing()
    {
        base.OnAppearing();
        mealViewModel = BindingContext as MealViewModel; // It may have changed
        mealViewModel.ShowLineItemsHint = App.Settings is not null && App.Settings.ShowLineItemsHint;
        mealViewModel.LineItemAddCompletedInUi = LineItemAddCompletedInUi;
        ArrangeSharesButtons(); // In case the sharer list changed
        if (Command is not null && Command.Equals("SelectFirstUnallocatedLineItem"))
            SelectFirstUnallocatedLineItem();
        var li = (LineItem)LineItemsListView.SelectedItem;
        if (li is not null)
            DrawAllSharesButtons(li);
        mealViewModel.SharingChanged += MealViewModel_SharingChanged;
        mealViewModel.ScrollLineItemsTo = ScrollLineItemsTo;
    }
    protected override void OnDisappearing()
    {
        mealViewModel.SharingChanged -= MealViewModel_SharingChanged;
        Meal.RequestSnapshot();
        mealViewModel.ForgetDeletedItems();
        base.OnDisappearing();
        mealViewModel.ScrollLineItemsTo = null;
    }
    private void MealViewModel_SharingChanged(LineItem li)
    {
        if (li == (LineItem)LineItemsListView.SelectedItem)
        {
            SharesCountContainer.IsVisible = false;
            DrawAllSharesButtons(li);
        }
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
            int buttonNumber = 0; // numbered from the left 0, 1, 2...
            foreach (var c in mealViewModel.Costs)
            {
                var button = ShareButtons[c.DinerIndex];
                button.BorderWidth = 0;
                button.IsVisible = true;
                button.CommandParameter = c;
                button.Text = buttonNumber == c.DinerIndex ? c.Nickname : c.DinerIDText + c.Nickname;
                button.WidthRequest = buttonWidth;
                buttonNumber++;
            }
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            return; // The button layout may be messed up, but the program will run just fine
        }
    }
    private static void DrawSharesButton(Button btn, byte? shares)
    {
        btn.BorderWidth = shares > 0 ? 2 : 0;
        btn.CornerRadius = shares > 1 ? 20 : 0;
    }

    private void DrawAllSharesButtons(LineItem li)
    {
        if (li is null || ShareButtons is null) return;
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
    private async void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        LineItem priorLi = e.PreviousSelection.Count == 0 ? null : (LineItem)e.PreviousSelection[0];
        if (priorLi is not null)
        {
            // Hide the prior item if it should no longer be visible
            mealViewModel.LineItemDeselected(priorLi);
        }

        var li = e.CurrentSelection.Count == 0 ? null : (LineItem)e.CurrentSelection[0];
        ItemEntryContainer.IsVisible = li is not null;
        totalsContainer.IsVisible = li is null;
        SharesCountContainer.IsVisible = false;
        CurrentSharesButton = null;
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

    private Button CurrentSharesButton = null;
    private byte CurrentShares = 0;
    private void OnSharesCountButtonClicked(object sender, EventArgs e)
    {
        var li = (LineItem)LineItemsListView.SelectedItem;
        var btn = sender as Button;
        if (li is not null && CurrentPersonCost is not null)
        {
            CurrentShares = byte.Parse(btn.Text);
            li.SetShares(sharerID: CurrentPersonCost.DinerID, CurrentShares);
            UpdateSharesInfoHeaderText(CurrentShares);
            DrawSharesButton(CurrentSharesButton, CurrentShares);
        }
    }
    private void UpdateSharesInfoHeaderText(byte shares)
    {
        SharesCountHeader.Text = CurrentPersonCost.Nickname + ": " + shares + " share";
        if (shares != 1) SharesCountHeader.Text += "s";
    }
    public void SelectFirstUnallocatedLineItem()
    {
        mealViewModel.ClearFiltering();
        var li = mealViewModel.LineItems.FirstOrDefault(li2 => li2.TotalSharers == 0);
        if (li is not null)
            LineItemsListView.SelectedItem = li;
    }
    private void OnVenueNameTapped(object sender, TappedEventArgs e) => App.PushAsync(Routes.PropertiesPage);

    #region Collection Scrolling
    private void ScrollLineItemsTo(int index, bool toEnd) // Passed in to viewModel
=> LineItemsListView.ScrollTo(index, position: toEnd ? ScrollToPosition.End : ScrollToPosition.Start);
    private void OnCollectionViewScrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        mealViewModel.FirstVisibleItemIndex = e.FirstVisibleItemIndex;
        mealViewModel.LastVisibleItemIndex = e.LastVisibleItemIndex;
    }
    #endregion
}
