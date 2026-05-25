using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Models;
using DivisiBill.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace DivisiBill.ViewModels;

public partial class MealViewModel : ObservableObjectPlus
{
    #region Nested Classes
    internal class ShareInfo // would be a record if I could use C#9
    {
        public LineItem LineItem;
        public byte Shares;
    }

    internal class SavedCost
    {
        public PersonCost PersonCost;
        public List<ShareInfo> ShareInfoList = [];
    }
    #endregion
    #region Initialization and Termination
    public MealViewModel()
    {
        Meal.CurrentMeal.PropertyChanged += CurrentMeal_PropertyChanged;
        SubscribeToCosts(Costs);
        LineItems = GetLineItems();
        DefaultTipRatePercentage = App.Settings.DefaultTipRate;
        DefaultTaxRatePercentage = (decimal)(App.Settings.DefaultTaxRate * 100);
    }
    ~MealViewModel()
    {
        Meal.CurrentMeal.PropertyChanged -= CurrentMeal_PropertyChanged;
        UnsubscribeFromCosts();
    }
    public void LoadLineItem()
    {
        LoadLineItemAmount();
        LoadLineItemNameString();
    }
    public void UnloadLineItem()
    {
        UnloadLineItemAmount();
        UnloadLineItemNameString();
    }
    #endregion
    #region Property Change Events
    public void CurrentMeal_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
        if (e.PropertyName.Equals(nameof(Meal.AmountForSharerID)))
        {
            OnPropertyChanged(nameof(IsFiltered));
            OnPropertyChanged(nameof(FilterGlyph));
            OnPropertyChanged(nameof(FilteredSharerName));
        }
        else if (e.PropertyName.Equals(nameof(Meal.VenueName)))
            OnPropertyChanged(nameof(CurrentVenue));
        else if (e.PropertyName.Equals(nameof(Meal.TipRate)))
        {
            OnPropertyChanged(nameof(IsDefaultTip));
            OnPropertyChanged(nameof(IsDefaultTipRate));
        }
        else if (e.PropertyName.Equals(nameof(Meal.TipDelta)))
        {
            OnPropertyChanged(nameof(IsDefaultTip));
        }
        else if (e.PropertyName.Equals(nameof(Meal.TaxRate)))
        {
            OnPropertyChanged(nameof(IsDefaultTax));
            OnPropertyChanged(nameof(IsDefaultTaxRate));
        }
        else if (e.PropertyName.Equals(nameof(Meal.TipOnTax)))
        {
            OnPropertyChanged(nameof(IsDefaultTip));
            OnPropertyChanged(nameof(IsDefaultTipOnTax));
        }
        else if (e.PropertyName.Equals(nameof(Meal.IsCouponAfterTax)))
        {
            OnPropertyChanged(nameof(IsDefaultTax));
            OnPropertyChanged(nameof(IsDefaultCouponAfterTax));
            if (IsFiltered)
            {
                DistributeCostsIfNeeded();
                SetFilteredBlockTotals();
            }
        }
        else if (e.PropertyName.Equals(nameof(Meal.UnallocatedAmount)))
        {
            OnPropertyChanged(nameof(IsAnyUnallocated));
        }
        else if (e.PropertyName.Equals(nameof(Meal.CreationTime)))
        {
            OnPropertyChanged(nameof(DefaultFileName));
        }
        else if (e.PropertyName.Equals(nameof(Meal.LastChangeTime)))
        {
            OnPropertyChanged(nameof(IsLastChangeTimeDifferent));
            OnPropertyChanged(nameof(LastChangeTimeText));
        }
        else if (e.PropertyName.Equals(nameof(Meal.IsCouponAfterTax)))
        {
            OnPropertyChanged(nameof(SubTotal));
        }
        else if (e.PropertyName.Equals(nameof(Meal.SubTotal)))
        {
            if (IsFiltered)
                SetFilteredSubtotal();
        }
        else if (e.PropertyName.Equals(nameof(Meal.Costs)))
        {
            UnsubscribeFromCosts();
            SubscribeToCosts(Costs);
            NotifyIsAnyCostsChanged();
        }
    }
    #endregion
    #region Object Independent
    [RelayCommand]
    public async Task PushVenueList() => await App.PushAsync(Routes.VenueListByNamePage);

    [RelayCommand]
    public async Task PushProperties() => await App.PushAsync(Routes.PropertiesPage);
    #endregion
    #region PersonCost
    #region PersonCost General Commands
    [RelayCommand]
    public void UndeleteCost()
    {
        if (IsAnyDeletedCost)
        {
            SavedCost sc = deletedCosts.Pop();
            InsertCost(sc.PersonCost);
            foreach (ShareInfo si in sc.ShareInfoList)
                si.LineItem.SetShares(sc.PersonCost.DinerID, si.Shares);
            IsAnyDeletedCost = deletedCosts.Count > 0;
            DistributeCostsIfNeeded();
        }
    }

    [RelayCommand]
    private void UndeleteAllCosts()
    {
        if (IsAnyDeletedCost)
        {
            while (deletedCosts.Count > 0)
                UndeleteCost();
        }
    }

    [RelayCommand]
    public void ForgetDeletedCosts()
    {
        deletedCosts.Clear();
        IsAnyDeletedCost = false;
    }

    [RelayCommand(CanExecute = nameof(IsAnyCosts))]
    public void DeleteCost(PersonCost pc)
    {
        if (pc is null)
            CostListDeleteAll();
        else if (pc == SelectedCost)
        {
            PersonCost next = Costs.Alternate(SelectedCost);
            CostListDelete(SelectedCost);
            SelectedCost = next;
        }
        else
            CostListDelete(pc);
    }

#if WINDOWS
    private PersonCost LastCostSelectedByMe { get; set; } = null; // Part of a workaround used by SelectCost below
#endif

    [RelayCommand]
    public void SelectCost(PersonCost pc)
    {
#if WINDOWS
        // Unfortunately Windows selects any new item before calling this code
        // probably related to https://github.com/dotnet/maui/issues/5446
        // This kludge works around that as long as you only use this method for selection
        if (pc == LastCostSelectedByMe)
        {
            SelectedCost = null;
            LastCostSelectedByMe = null;
        }
        else
        {
            SelectedCost = pc;
            LastCostSelectedByMe = pc;
        }
#else        
        if (pc == SelectedCost)
            SelectedCost = null;
        else if (pc is not null)
            SelectedCost = pc;
#endif
    }

    [RelayCommand]
    public void DeselectCost(PersonCost pc) => SelectedCost = null;

    [ObservableProperty]
    public partial PersonCost SelectedCost { get; set; }

    [RelayCommand]
    private async Task ShowPerson(PersonCost pc)
    {
        if (pc is null)
            return;
        if (pc.Diner is null)
        {
            // This Meal (and we know it's the current one) hasn't been changed yet, so it has not been
            // reconciled with the lists of people and venues, so just do it now
            Meal.CurrentMeal.UpdateOtherLists();
            await Person.SaveSettingsIfChangedAsync();
            if (!Venue.IsSaved)
                await Venue.SaveSettingsAsync();
        }

        if (pc.Diner is not null)
        {
            ShellNavigationQueryParameters navigationParameter = new()
            {
                    { "TargetPerson", pc.Diner }
            };
            await App.PushAsync(Routes.PersonEditPage, navigationParameter);
        }
        else // should never happen, but just in case...
            await Services.Utilities.DisplayAlertAsync("Unknown Person", "Sorry, there's no existing person entry corresponding to " + pc.Nickname
                + ". Edit the bill and we'll find (or create) one.");
    }
    [RelayCommand]
    private async Task DisplayPayments(PersonCost pc)
    {
        if (pc is null && IsFiltered)
            pc = FilteredSharer;
        await Utilities.ShowPayments(new PaymentsViewModel(SubTotal + Tax - (IsCouponAfterTax ? RawCouponAmount : 0),
            RoundedAmount, pc?.Nickname, pc is null ? 0 : RoundedAmount - Math.Round(pc.Amount), UnallocatedAmount));
    }
    [RelayCommand]
    private async Task DisplayPaymentsForLineItem(LineItem li)
    {
        PersonCost pc = null;
        if (li is not null && !IsFiltered)
            pc = Meal.CurrentMeal.Costs.FirstOrDefault(personCost => personCost.DinerID == li.FirstSharer);
        await DisplayPayments(pc);
    }

    [RelayCommand(CanExecute = nameof(IsAnyCosts))]
    public async Task FilterItems(PersonCost pc)
    {
        if (pc is null) // No idea who this could be for so just cycle through all 
        {
            PersonCost next = Meal.CurrentMeal.GetNextPersonCost(FilteredSharer);
            AmountForSharerID = next is null ? LineItem.DinerID.none : next.DinerID;
            if (next is not null)
                await GoToItemsAsync();
        }
        else if (AmountForSharerID == pc.DinerID) // We're already filtering for this participant, so stop 
            ClearFiltering();
        else
        {
            AmountForSharerID = pc.DinerID; // turn on filtering for this PersonCost
            await GoToItemsAsync();
        }
    }

    [RelayCommand]
    public async Task GoToItemsAsync() => await App.GoToAsync(Routes.LineItemsPage);

    [RelayCommand]
    public async Task Mail(PersonCost pc) => await Meal.CurrentMeal.CreateEmailMessageAsync(pc);

    [RelayCommand]
    private async Task ShowUnallocated() => await App.GoToAsync(Routes.LineItemsPage + "?command=SelectFirstUnallocatedLineItem");

    #endregion
    #region PersonCost Drag and Drop Commands
    private PersonCost draggedPersonCost;

    [RelayCommand]
    private void DragStartingPersonCost(PersonCost personCost)
    {
        Utilities.DebugMsg($"Enter DragStartingPersonCost for {personCost.Nickname}");
        if (Costs.Count >= LineItem.maxSharers) // We need one empty slot for temporary storage
        {
            Utilities.DisplayAlertAsync("Error", $"Sorry, drag and drop is not allowed with over {LineItem.maxSharers - 1} participants");
            return;
        }
        if (personCost != null)
        {
            draggedPersonCost = personCost;
        }
    }

    [RelayCommand]
    private void DropPersonCost(PersonCost targetCost)
    {
        Utilities.DebugMsg($"Entering DropPersonCost for {targetCost.Nickname}");
        if (draggedPersonCost != null && targetCost != null && draggedPersonCost != targetCost)
        {
            int draggedIndex = Costs.IndexOf(draggedPersonCost);
            int targetIndex = Costs.IndexOf(targetCost);

            if (draggedIndex != -1 && targetIndex != -1 && draggedIndex != targetIndex)
            {
                MovePersonCost(draggedIndex, targetIndex);
                if (!CostListResequence()) // Ensure DinerIDs are in order after the move
                    MovePersonCost(targetIndex, draggedIndex); // It did not work, put the dragged item back where it was and give up
                #region This is a hack to force the display to update correctly, see https://github.com/dotnet/maui/issues/35599
                var temp = Costs; // Alias for Meal.CurrentMeal.Costs
                Meal.CurrentMeal.Costs = null;
                Meal.CurrentMeal.Costs = temp;
                #endregion
            }
        }
        draggedPersonCost = null;
    }
    #endregion
    #region Assorted Properties and Methods
    private readonly Stack<SavedCost> deletedCosts = new();
    private bool InsertCost(PersonCost pc)
    {
        int endInx = Costs.Count - 1; // Last element
        if (endInx + 1 >= LineItem.maxSharers)
            return false; // The list is already full

        if (endInx < 0 // costs list is empty, the merge is trivial
            || (pc.DinerIndex >= endInx + 1)) // after the last element, so there's an unused DinerID available
        {
            pc.DinerID = (LineItem.DinerID)(endInx + 2); // Force it to use the next ID
            Costs.Add(pc);
            return true;
        }
        else // The more complex case of adding somewhere within the list
        {
            int costInx = pc.DinerIndex;
            // Find the slot contained the ID we want or the one before it (the list is ordered and sequential)
            if (Costs[costInx].DinerID == pc.DinerID) // The CostIndex has been reused
            {
                //The list could have been resequenced or a new item added, either way, just insert this where it was before, moving everything after down one
                for (int unusedCostInx = endInx + 1; unusedCostInx > costInx; unusedCostInx--) // first move down all the ones including and after the slot we want
                {
                    PersonCostRenumber(Costs[unusedCostInx - 1], (LineItem.DinerID)(unusedCostInx + 1));
                }
                // Now we have opened up an empty slot so we'll just be able to insert there
                Costs.Insert(costInx, pc); // Insert the new diner in the newly emptied slot
            }
            else // The slot at costInx contains the first ID that is smaller than the one we are inserting
                Costs.Insert(costInx + 1, pc); // Insert the new diner after the one with a smaller DinerId
            return true;
        }
    }
    public void DistributeCostsIfNeeded()
    {
        if (!Meal.CurrentMeal.IsDistributed)
            Meal.CurrentMeal.DistributeCosts();
    }
    public bool IsAnyDeletedCost
    {
        get;

        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(IsAnyDeletedCost));
            }
            // Always recheck IsManyDeletedCosts because for it transitions between {0,1} and {2+} are what count 
            OnPropertyChanged(nameof(IsManyDeletedCosts));
        }
    } = false;
    public bool IsManyDeletedCosts => deletedCosts.Count > 1;
    public ObservableCollection<PersonCost> Costs => Meal.CurrentMeal.Costs;
    public PersonCost CostListAdd(Person p) => Meal.CurrentMeal.CostListAdd(p);
    public void CostListDelete(PersonCost pc)
    {
        if (pc.DinerID == AmountForSharerID)
        {
            // We are about to delete the filtered sharer so turn off filtering first
            ClearFiltering();
        }
        SavedCost sc = new() { PersonCost = pc };
        foreach (LineItem li in LineItems.Where((li) => li.SharedBy[pc.DinerIndex]))
        {
            ShareInfo si = new() { LineItem = li, Shares = li.GetShares(pc.DinerID) };
            sc.ShareInfoList.Add(si);
        }
        deletedCosts.Push(sc);
        Meal.CurrentMeal.CostListDelete(pc);
        Meal.CurrentMeal.CostListResequence();
        IsAnyDeletedCost = true;
    }
    public void CostListDeleteAll()
    {
        foreach (PersonCost pc in new List<PersonCost>(Meal.CurrentMeal.Costs))
            CostListDelete(pc);
    }
    public void MovePersonCost(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Costs.Count || newIndex < 0 || newIndex >= Costs.Count)
            return;

        if (oldIndex == newIndex)
            return;

        Costs.Move(oldIndex, newIndex);
    }

    private void PersonCostRenumber(PersonCost pcToChange, LineItem.DinerID newUnusedDinerID)
    {
        // Validity check - ensure the new ID is unused
        if (null != Costs.FirstOrDefault(pc => pc.DinerID == newUnusedDinerID))
            return;
        LineItem.DinerID oldDinerID = pcToChange.DinerID;
        pcToChange.DinerID = newUnusedDinerID; // Important to do this first
        foreach (LineItem li in LineItems.Where(li => li.GetShares(oldDinerID) > 0))
            li.TransferShares(newSharerID: newUnusedDinerID, oldSharerID: oldDinerID);
    }
    // Handy record type to track the information we need for resequencing the cost list, we will build a list of these and then process them in order to do the resequencing
    [DebuggerDisplay("{DebuggerText}")]
    private class PersonCostInfo(PersonCost pc, LineItem.DinerID currentID, LineItem.DinerID desiredID)
    {
        public PersonCost pc = pc;
        public LineItem.DinerID currentID = currentID;
        public LineItem.DinerID desiredID = desiredID;
        public string DebuggerText => $"{pc.Nickname} ({pc.DinerIndex + 1}) - {currentID} -> {desiredID}";
    }

    /// <summary>
    /// Resequences the cost list to ensure DinerIDs are sequential starting from the first identifier.
    /// </summary>
    /// <remarks>Passes through the PersonCost items determining which should be renumbered and then moving them in order so that each move frees 
    /// a desirable number (DinerID). That permits us to move whoever gets that number, so they free up another number, and so on. If the number that is
    /// freed up happens to be outside the target range (1 thru count of items) we just move on to an arbitrary item and if its desired number is taken we
    /// move the item that is currently using the Target item to a DinerID outside the target range.</remarks>
    /// <returns>true if resequencing succeeded or was unnecessary; false if the operation failed.</returns>
    public bool CostListResequence()
    {
        try
        {
            // Initial easy decisions
            if (Costs.Count == 0)
                return false; // Nothing to do!
            // No sorting required for the trivial case but we may still need to fix up the number if the one item is not using the first DinerID
            if (Costs.Count == 1)
            {
                // Just one item, so just make sure it is using the first DinerID
                if (Costs[0].DinerID != LineItem.DinerID.first)
                    PersonCostRenumber(Costs[0], LineItem.DinerID.first);
                return true;
            }

            // On to the non-trivial case of multiple items, start by initializing a list of which DinerIDs are currently in use and which are not,
            // we will need this to know which items need to be moved and to find temporary slots for moving items around
            EnumAvailabilityArray<LineItem.DinerID> Unused = new();
            Unused[LineItem.DinerID.limit] = false; // The limit value is not a valid DinerID and should never be used, so mark it as unavailable to simplify the logic

            // Build list of items that need resequencing and record which DinerIDs are currently in use.
            var itemsToMove = new List<PersonCostInfo>();
            LineItem.DinerID desiredID = LineItem.DinerID.first;
            foreach (PersonCost pc in Costs)
            {
                Unused[pc.DinerID] = false; // Mark this ID as used
                if (pc.DinerID != desiredID)
                    itemsToMove.Add(new(pc, pc.DinerID, desiredID));
                desiredID++;
            }

            // another trivial case is that all items are using a desirable DinerID, so no sorting or moving needed
            if (itemsToMove.Count == 0)
                return true; // Nothing to do!

            // We need to do some reordering so we need at least one unused DinerID to use as temporary storage
            if (Costs.Count >= LineItem.maxSharers)
                throw new Exception("In MealViewModel.CostListResequence, costs list has " + Costs.Count + " items, which exceeds the maximum we can resequence of " + LineItem.maxSharers);

            // All the easy outs are gone, we must actually start reallocating things.
            LineItem.DinerID largestDesiredID = desiredID - 1;

            // Local utility function to do a renumber and update the tracking variables accordingly
            void Renumber(PersonCost itemToRenumber, LineItem.DinerID oldID, LineItem.DinerID newID)
            {
                PersonCostRenumber(itemToRenumber, newID);
                Unused[oldID] = true;
                Unused[newID] = false;
            }

            // Step through the list, handle the easy cases where the desirable DinerID is not currently being used by just moving the item there, which will free up its
            // current DinerID for another item that needs it. This will also reduce the number of items we need to move in the more complex cases, where we will
            // use a temporary slot to free up the desirable DinerID.
            foreach (var item in itemsToMove.OrderBy(x => x.desiredID))
            {
                // Because we're stepping through the list in order of desirable DinerID, we know that any target desirable DinerID that is currently in use must be being
                // used by an item that has not yet been moved and is therefore still in the list of items to move, so we can just check that list to find the item that is
                // currently using the desirable DinerID for this item
                if (item.desiredID == item.currentID)
                    continue; // This item has already been moved to desired DinerID, so nothing to do for this item
                if (Unused[item.desiredID])
                {
                    // the destination DinerID is available, so we can just move this item there and be done with it
                    Renumber(item.pc, item.currentID, item.desiredID);
                    continue;
                }
                else
                {
                    // the destination DinerID is not available, so we need to move the item that is currently using it to a temporary slot first to free up the desirable DinerID for this item
                    var target = itemsToMove.FirstOrDefault(x => x.currentID == item.desiredID);
                    var tempID = Unused.GetHighestAvailable();
                    if (tempID is null or LineItem.DinerID.none)
                        throw new Exception("In MealViewModel.CostListResequence, no unused DinerID available for temporary assignment");
                    // Now swap the two items using a temporary slot
                    Renumber(target.pc, target.currentID, tempID.Value);
                    Renumber(item.pc, item.currentID, item.desiredID);
                    Renumber(target.pc, tempID.Value, item.currentID);
                    // The target item now has the DinerID that the item formerly had and vice versa, so update the currentID values to reflect that change,
                    // this will ensure that when we need to move the target item later we will be able to find it in the list by its new current DinerID
                    (target.currentID, item.currentID) = (item.currentID, target.currentID);
                }
            }
            // validate the new order because the preceding code has a lot of moving parts and it's easy to make a mistake that would leave the list in an invalid state,
            // so we'll check that the DinerIDs are now in the desired order with no duplicates or gaps
            desiredID = LineItem.DinerID.first;
            foreach (PersonCost pc in Costs)
            {
                if (pc.DinerID != desiredID)
                    throw new Exception($"Resequence failed to properly reorder costs, expected DinerID {desiredID} for \"{pc.Nickname}\" but found {pc.DinerID}");
                desiredID++;
            }
            return true; // Success!
        }
        catch (Exception ex)
        {
            ex.ReportCrash("Exception in MealViewModel.CostListResequence");
            return false; // Fail - exception
        }
    }
    #endregion
    #endregion
    #region LineItem
    #region Data Entry
    #region Item Selection

    [ObservableProperty]
    public partial LineItem SelectedLineItem { get; set; }

    partial void OnSelectedLineItemChanging(LineItem oldValue, LineItem newValue)
    {
        if (oldValue is not null && IsValidLineItemAmount)
            UnloadLineItem();
    }
    partial void OnSelectedLineItemChanged(LineItem value)
    {
        if (value is not null)
            LoadLineItem();
        FilterItemsFromLineItemCommand.NotifyCanExecuteChanged();
        DuplicateLineItemCommand.NotifyCanExecuteChanged();
        ChangeSharingCommand.NotifyCanExecuteChanged();
    }
    public LineItem SelectedOrFirstLineItem => SelectedLineItem ?? LineItems.FirstOrDefault();

    /// <summary>
    /// Implements a command to select or Deselect the current LineItem
    /// BEWARE because of bug https://github.com/dotnet/maui/issues/5446 this may be called before or after LineItemsPage.OnItemSelected/>
    /// Amazingly, this depends on whether a Button or Rectangle is used, if it is a Button, everything is fine, but for a Rectangle
    /// it is called before on Android, after on Windows on .NET 8 and 9 at least.
    /// </summary>
    /// <param name="li"></param>
    [RelayCommand]
    private void ToggleSelectLineItem(LineItem li) => SelectedLineItem = SelectedLineItem == li ? null : li;

    [RelayCommand]
    private void DeselectAllLineItems(LineItem li) => SelectedLineItem = null;
    private LineItem AddItem(LineItem li)
    {
        li ??= new(); // Should never happen, but just in case
        // Insert after the selected item or at the end if none selected
        li.EnsureItemName(); // Give the item a default name if it doesn't have a name
        LineItems.InsertAfter(SelectedLineItem, li);
        if (IsFiltered)
        {
            if (li.GetShares(AmountForSharerID) < 1)
                li.SetShares(AmountForSharerID, 1);
            Meal.CurrentMeal.LineItems.InsertAfter(SelectedLineItem, li); // because the one in LineItems is temporary.
            DistributeCostsIfNeeded();
            SetFilteredBlockTotals();
        }
        return li;
    }

    [RelayCommand]
    public void AddLineItem(LineItem li)
    {
        SelectedLineItem = AddItem(li);
        NotifyLineItemAddCompleted(li);
    }

    bool CanDuplicateLineItem() => SelectedLineItem is not null || LineItems.FirstOrDefault() is not null;

    [RelayCommand(CanExecute = nameof(CanDuplicateLineItem))]
    public void DuplicateLineItem()
    {
        LineItem li = SelectedLineItem ?? LineItems.LastOrDefault();
        LineItem newLi = li is null ? new LineItem() : new LineItem(li);
        AddItem(newLi);
        SelectedLineItem = newLi;
    }
    public Action<LineItem> LineItemAddCompletedInUi { get; set; }
    /// <summary>
    /// Notify the UI that an add action has completed in case it wants to set focus.
    /// </summary>
    private void NotifyLineItemAddCompleted(LineItem value) => LineItemAddCompletedInUi?.Invoke(value);
    #endregion
    #region Item Amount
    private void LoadLineItemAmount() => LineItemAmount = SelectedLineItem?.Amount ?? 0;

    [RelayCommand]
    private void UnloadLineItemAmount()
    {
        if (SelectedLineItem is not null && IsValidLineItemAmount)
            SelectedLineItem.Amount = LineItemAmount;
    }
    [RelayCommand]
    private void CompletedLineItemAmount()
    {
        if (SelectedLineItem is null)
            return; // nothing to do
        // Store the value if it is valid
        if (IsValidLineItemAmount)
            SelectedLineItem.Amount = LineItemAmount;
        // We only move to the next item on the desktop because the soft keyboard on a phone takes up so much space that it obscures the item list
        if (DeviceInfo.Current.Idiom == DeviceIdiom.Desktop)
        {
            // Select the next item in the list or switch to the first one if this is the last one
            SelectedLineItem = LineItems.Alternate(SelectedLineItem);
        }
    }

    [ObservableProperty]
    public partial decimal LineItemAmount { get; set; }
    #region ItemName
    public bool IsValidLineItemAmount { get; set; } = false;
    #endregion
    private void LoadLineItemNameString() => LineItemNameString = SelectedLineItem.ItemName;

    [RelayCommand]
    private void UnloadLineItemNameString() => SelectedLineItem?.ItemName = LineItemNameString;

    [ObservableProperty]
    public partial string LineItemNameString { get; set; }
    #endregion
    #endregion
    #region Delete and UnDelete
    private readonly Stack<LineItem> deletedLineItems = new();
    public LineItem DeleteItem(LineItem li)
    {
        LineItem alternate = LineItems.Alternate(li);
        if (!IsFiltered)
        {
            LineItems.Remove(li);
            deletedLineItems.Push(li);
            IsAnyDeletedLineItem = true;
            return alternate;
        }
        else
        {
            // We are filtering, so do not remove the item from Meal.LineItems, just turn off that participant's share of it
            li.SetShares(sharerID: AmountForSharerID, 0);
            // Remove the item from the currently visible list because it's not shared by this participant anymore
            LineItems.Remove(li);
            return alternate;
        }
    }
    public void RemoveAllLineItems()
    {
        if (Meal.CurrentMeal.AmountForSharerID == LineItem.DinerID.none)
        {
            foreach (LineItem li in LineItems.Reverse())
                deletedLineItems.Push(li);
            LineItems.Clear();
            IsAnyDeletedLineItem = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteLineItem))]
    public void DeleteLineItem(LineItem li)
    {
        li ??= SelectedLineItem;
        LineItem nextItem = null;
        if (li is null)
            RemoveAllLineItems();
        else
            nextItem = DeleteItem(li);
        SelectedLineItem = nextItem;
    }
    public bool CanDeleteLineItem(LineItem li) => li is not null || (LineItems is not null && LineItems.Count > 0);
    public void LineItemDeselected(LineItem li)
    {
        if (IsFiltered && li.GetShares(AmountForSharerID) < 1)
            LineItems.Remove(li);
    }

    public bool CanUndeleteLineItem() => IsAnyDeletedLineItem;

    [RelayCommand(CanExecute = nameof(CanUndeleteLineItem))]
    private void UndeleteLineItem()
    {
        if (IsAnyDeletedLineItem)
        {
            LineItem li = deletedLineItems.Pop();
            IsAnyDeletedLineItem = deletedLineItems.Count > 0;
            AddLineItem(li);
        }
    }

    public bool CanUndeleteAllLineItems() => IsManyDeletedLineItems;

    [RelayCommand(CanExecute = nameof(CanUndeleteAllLineItems))]
    private void UndeleteAllLineItems()
    {
        if (IsAnyDeletedLineItem)
        {
            LineItem firstUndeletedItem = deletedLineItems.Peek();
            while (deletedLineItems.Count > 0)
                AddItem(deletedLineItems.Pop());
            IsAnyDeletedLineItem = false;
            SelectedLineItem = firstUndeletedItem;
        }
    }
    public void ForgetDeletedItems()
    {
        deletedLineItems.Clear();
        IsAnyDeletedLineItem = false;
    }
    public bool IsAnyDeletedLineItem
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(IsAnyDeletedLineItem));
            }
            // Always recheck IsManyDeletedLineItems because for it transitions between {0,1} and {2+} are what count 
            OnPropertyChanged(nameof(IsManyDeletedLineItems));
            UndeleteAllLineItemsCommand.NotifyCanExecuteChanged();
            UndeleteLineItemCommand.NotifyCanExecuteChanged();
        }
    }
    public bool IsManyDeletedLineItems => deletedLineItems.Count > 1;

    #region Scrolling LineItem list
    [ObservableProperty]
    public partial bool IsLineItemSwipeUpAllowed { get; set; }

    [ObservableProperty]
    public partial bool IsLineItemSwipeDownAllowed { get; set; }

    [ObservableProperty]
    public partial int FirstVisibleItemIndex { get; set; }

    partial void OnFirstVisibleItemIndexChanged(int value)
    {
        IsLineItemSwipeDownAllowed = value > 0;
    }

    [ObservableProperty]
    public partial int LastVisibleItemIndex { get; set; }

    partial void OnLastVisibleItemIndexChanged(int value)
    {
        IsLineItemSwipeUpAllowed = value > 0 && value < LineItems.Count - 1;
    }

    public Action<int, bool> ScrollLineItemsTo = null;

    [RelayCommand]
    private void ScrollItems(string whereTo)
    {
        if (FirstVisibleItemIndex == LastVisibleItemIndex || ScrollLineItemsTo is null || LineItems is null)
            return;
        int lastItemIndex = LineItems.Count - 1;
        if (lastItemIndex < 2)
            return;
        try
        {
            switch (whereTo)
            {
                case "Up":
                    if (LastVisibleItemIndex < lastItemIndex)
                        ScrollLineItemsTo(LastVisibleItemIndex, false); break;
                case "Down":
                    if (FirstVisibleItemIndex > 0)
                        ScrollLineItemsTo(FirstVisibleItemIndex, true); break;
                case "End":
                    if (LastVisibleItemIndex < lastItemIndex)
                        ScrollLineItemsTo(lastItemIndex, false); break;
                case "Start":
                    if (FirstVisibleItemIndex > 0)
                        ScrollLineItemsTo(0, true); break;
                default: break;
            }
        }
        catch (Exception ex)
        {
            ex.ReportCrash("fault attempting to scroll");
            // Do nothing, we do not really care if a scroll attempt fails
        }
    }
    #endregion
    #endregion
    #region Commands
    private ObservableCollection<LineItem> GetLineItems() => IsFiltered
    ? [.. Meal.CurrentMeal.LineItems.Where(li => li.IsSharedByFilter)]
    : Meal.CurrentMeal.LineItems;

    [ObservableProperty]
    public partial ObservableCollection<LineItem> LineItems { get; set; } = null;

    partial void OnLineItemsChanged(ObservableCollection<LineItem> oldValue, ObservableCollection<LineItem> newValue)
    {
        // Unsubscribe from old collection
        if (oldValue is not null)
            oldValue.CollectionChanged -= LineItems_CollectionChanged;

        // Subscribe to new collection
        if (newValue is not null)
            newValue.CollectionChanged += LineItems_CollectionChanged;

        DeleteLineItemCommand.NotifyCanExecuteChanged();
    }

    private void LineItems_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        DeleteLineItemCommand.NotifyCanExecuteChanged();
        FilterItemsFromLineItemCommand.NotifyCanExecuteChanged();
        DuplicateLineItemCommand.NotifyCanExecuteChanged();
    }

    public void MoveLineItem(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= LineItems.Count || newIndex < 0 || newIndex >= LineItems.Count)
            return;

        if (oldIndex == newIndex)
            return;

        LineItems.Move(oldIndex, newIndex);
    }
    #region LineItem Drag and Drop Commands
    private LineItem draggedLineItem;

    [RelayCommand]
    private void DragStarting(LineItem lineItem)
    {
        if (lineItem != null)
        {
            draggedLineItem = lineItem;
        }
    }

    [RelayCommand]
    private void Drop(LineItem targetItem)
    {
        if (draggedLineItem != null && targetItem != null && draggedLineItem != targetItem)
        {
            int draggedIndex = LineItems.IndexOf(draggedLineItem);
            int targetIndex = LineItems.IndexOf(targetItem);

            if (draggedIndex != -1 && targetIndex != -1 && draggedIndex != targetIndex)
            {
                MoveLineItem(draggedIndex, targetIndex);
                #region This is a hack to force the display to update correctly, see https://github.com/dotnet/maui/issues/35599
                var temp = LineItems;
                LineItems = null;
                LineItems = temp;
                #endregion
            }
        }
        draggedLineItem = null;
    }
    #endregion
    public void ChangeShares(LineItem li)
    {
        if (li.TotalSharers == 0)
            li.DistributeCouponValue(Meal.CurrentMeal);
        else
            li.DeallocateShares();
        if (IsFiltered)
            DistributeCostsIfNeeded();
    }
    public enum ChangeType
    {
        Even,
        Proportional,
        Clear,
        Cycle
    }
    public void ChangeSharing(LineItem li, ChangeType changeType)
    {
        if (li is not null)
        {
            if (changeType == ChangeType.Cycle)
            {
                changeType = li.GetSharingType() switch
                {
                    LineItem.SharingType.Even => ChangeType.Proportional,
                    LineItem.SharingType.Uneven => ChangeType.Clear,
                    _ => ChangeType.Even
                };
            }
            switch (changeType)
            {
                case ChangeType.Even:
                    li.ShareEvenly(Costs);
                    break;
                case ChangeType.Proportional:
                    DistributeCostsIfNeeded(); // because this depends on accurate costs
                    li.DistributeCouponValue(Meal.CurrentMeal);
                    break;
                case ChangeType.Clear:
                    li.DeallocateShares();
                    break;
            }
            if (IsFiltered)
                DistributeCostsIfNeeded();
            SharingChanged?.Invoke(li);
        }
    }

    [RelayCommand]
    public void ChangeComp(object param)
    {
        if (param is LineItem li)
        {
            if (li.Amount >= 0 || li.Comped)
                li.Comped = !li.Comped;
            else
                Utilities.DisplayAlertAsync("Error", "You cannot comp a coupon (negative item)");
        }
    }

    bool SelectedLineItemIsNotNull() => SelectedLineItem is not null;

    [RelayCommand(CanExecute = nameof(SelectedLineItemIsNotNull))]
    public void ChangeSharing(object param)
    {
        if (param is string changeTypeString && Enum.TryParse(changeTypeString, out ChangeType changeType))
            ChangeSharing(SelectedOrFirstLineItem, changeType);
    }

    /// <summary>
    /// Used for the in-line case where there need not be a selected item
    /// </summary>
    /// <param name="li"></param>
    [RelayCommand]
    public void ChangeSharingGesture(LineItem li)
    {
        ChangeSharing(li, ChangeType.Cycle);
    }
    public event Action<LineItem> SharingChanged;

    [RelayCommand]
    public async Task GoToTotals() => await App.GoToAsync(Routes.TotalsPage);

    [RelayCommand(CanExecute = nameof(IsAnyCosts))]
    public async Task Adjust()
    {
        IPopupResult<decimal> popupResult = await Shell.Current.ShowPopupAsync<decimal>
            (new Controls.AdjustPopup(new AdjustViewModel(SubTotal, Meal.CurrentMeal.TaxRate, TaxDelta, IsCouponAfterTax ? RawCouponAmount : 0)), Utilities.GetNullPopupOptions());
        if (!popupResult.WasDismissedByTappingOutsideOfPopup && popupResult?.Result is decimal adjustmentAmount && adjustmentAmount != 0)
        {
            DistributeCostsIfNeeded(); // because sharing depends on accurate costs
            var adjustmentLineItem = new LineItem() { ItemName = "Adjustment", Amount = adjustmentAmount };
            Meal.CurrentMeal.LineItems.Add(adjustmentLineItem);
            adjustmentLineItem.DistributeCouponValue(Meal.CurrentMeal);
            SelectedLineItem = adjustmentLineItem;
            DistributeCostsIfNeeded(); // to account for the adjustment amount
        }
    }

    public bool IsAnyCosts => Costs is not null && Costs.Count > 0;

    private ObservableCollection<PersonCost> _subscribedCosts;
    private void SubscribeToCosts(ObservableCollection<PersonCost> costs)
    {
        _subscribedCosts = costs;
        if (costs is not null)
            costs.CollectionChanged += OnCostsCollectionChanged;
    }
    private void UnsubscribeFromCosts()
    {
        if (_subscribedCosts is not null)
        {
            _subscribedCosts.CollectionChanged -= OnCostsCollectionChanged;
            _subscribedCosts = null;
        }
    }
    private void OnCostsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        int oldCount = Costs.Count - (e.NewItems?.Count ?? 0) + (e.OldItems?.Count ?? 0);
        if ((oldCount == 0) != (Costs.Count == 0))
            NotifyIsAnyCostsChanged();
    }
    private void NotifyIsAnyCostsChanged()
    {
        OnPropertyChanged(nameof(IsAnyCosts));
        DeleteCostCommand.NotifyCanExecuteChanged();
        FilterItemsCommand.NotifyCanExecuteChanged();
        AdjustCommand.NotifyCanExecuteChanged();
    }
    #endregion
    #endregion
    #region Totals, meal amounts and properties
    private decimal VisiblePositive => LineItems.Where(l => l.FilteredAmount > 0 && !l.Comped).Sum(l => l.FilteredAmount);

    private decimal VisibleNegative => -LineItems.Where(l => l.FilteredAmount < 0).Sum(l => l.FilteredAmount);
    public decimal SubTotal => Meal.CurrentMeal.SubTotal;
    private void SetFilteredSubtotal() => FilteredSubTotal = Math.Max(0, IsFiltered ? VisiblePositive - (IsCouponAfterTax ? 0 : VisibleNegative) : 0);
    [ObservableProperty]
    public partial decimal FilteredSubTotal { get; private set; }
    public decimal TotalAmount => Meal.CurrentMeal.TotalAmount;
    public decimal RoundedAmount => Meal.CurrentMeal.RoundedAmount;
    public bool IsAnyUnallocated => Meal.CurrentMeal.UnallocatedAmount != 0;
    public decimal UnallocatedAmount => Meal.CurrentMeal.UnallocatedAmount;
    public Venue CurrentVenue => Venue.FindVenueByName(Meal.CurrentMeal.VenueName);
    public DateTime CreationTime => Meal.CurrentMeal.CreationTime;
    public DateTime LastChangeTime => Meal.CurrentMeal.LastChangeTime;
    public string LastChangeTimeText => Meal.CurrentMeal.Summary.GetLastChangeString();
    public bool IsLastChangeTimeDifferent => !Utilities.WithinOneSecond(CreationTime, LastChangeTime);
    public decimal RoundingErrorAmount => Meal.CurrentMeal.RoundingErrorAmount;
    public bool IsUnsharedAmountSignificant => Meal.CurrentMeal.IsUnsharedAmountSignificant;
    public string DiagnosticInfo => Meal.CurrentMeal?.DiagnosticInfo ?? string.Empty;
    public string DefaultFileName => IsDefault ? null : Meal.CurrentMeal.FileName;
    #endregion
    #region Meal Data
    public string VenueName => Meal.CurrentMeal.VenueName;
    public double TaxRate => Meal.CurrentMeal.TaxRate * 100;
    public decimal Tax => Meal.CurrentMeal.Tax;
    public decimal TaxDelta => Meal.CurrentMeal.TaxDelta;
    public bool TipOnTax => Meal.CurrentMeal.TipOnTax;
    public int TipRate => Convert.ToInt32(Meal.CurrentMeal.TipRate * 100);
    public decimal Tip => Meal.CurrentMeal.Tip;
    public decimal TipDelta => Meal.CurrentMeal.TipDelta;
    public bool IsCouponAfterTax => Meal.CurrentMeal.IsCouponAfterTax;
    // Zeroing these out when unused makes the XAML simpler
    public decimal CouponAmountIfAfterTax => Meal.CurrentMeal.CouponAmountIfAfterTax;
    public decimal RawCouponAmount => Meal.CurrentMeal.GetRawCouponAmount();
    private void SetFilteredCouponAmountIfAfterTax() => FilteredCouponAmountIfAfterTax = IsFiltered && IsCouponAfterTax ? VisibleNegative : 0;
    [ObservableProperty]
    public partial decimal FilteredCouponAmountIfAfterTax { get; private set; }
    public decimal ScannedSubTotal => Meal.CurrentMeal.ScannedSubTotal;
    public decimal ScannedTax => Meal.CurrentMeal.ScannedTax;
    #endregion    
    #region Filtering for a sharer
    private void SetFilteredBlockTotals()
    {
        SetFilteredSubtotal();
        SetFilteredCouponAmountIfAfterTax();
    }
    /// <summary>
    /// Gets or sets the identifier of the diner for whom the amount is being calculated or displayed.
    /// </summary>
    /// <remarks>Changing this property updates the associated line items and may trigger recalculation of
    /// costs if filters are applied.</remarks>
    public LineItem.DinerID AmountForSharerID
    {
        get => Meal.CurrentMeal.AmountForSharerID;
        set
        {
            if (Meal.CurrentMeal.AmountForSharerID != value)
            {
                bool isFilteredChanged = IsFiltered != (value != LineItem.DinerID.none);
                Meal.CurrentMeal.AmountForSharerID = value;
                if (isFilteredChanged)
                {
                    OnPropertyChanged(nameof(IsFiltered));
                    ClearFilteringCommand.NotifyCanExecuteChanged();
                }
                LineItems = GetLineItems();
                if (IsFiltered)
                {
                    DistributeCostsIfNeeded();
                    SetFilteredBlockTotals();
                }
            }
        }
    }
    // The glyph to use - note it is inverted because it is showing what the glyph will do, not what the current state is
    public FontImageSource FilterGlyph => (FontImageSource)(IsFiltered ? Application.Current.Resources["GlyphFilterOff"] : Application.Current.Resources["GlyphFilterOn"]);
    public bool IsFiltered => AmountForSharerID != LineItem.DinerID.none;
    public string FilteredSharerName => IsFiltered ? FilteredSharer.Nickname : string.Empty;
    private PersonCost FilteredSharer => IsFiltered ? Costs.Where((pc) => pc.DinerID == AmountForSharerID).FirstOrDefault() : null;

    private LineItem previousFilteredLineItem = null;

    bool CanFilterFromLineItems => LineItems.Count > 0;

    [RelayCommand(CanExecute = nameof(CanFilterFromLineItems))]
    public void FilterItemsFromLineItem()
    {
        if (SelectedLineItem is null)
        {
            // No item selected, just iterate through all the costs
            PersonCost next = Meal.CurrentMeal.GetNextPersonCost(FilteredSharer);
            AmountForSharerID = next is null ? LineItem.DinerID.none : next.DinerID;
        }
        else if (SelectedLineItem != previousFilteredLineItem)
        {
            ClearFiltering();
            previousFilteredLineItem = SelectedLineItem;
        }
        if (SelectedLineItem is not null)
            AmountForSharerID = SelectedLineItem.GetNextSharer(AmountForSharerID);
    }

    [RelayCommand(CanExecute = nameof(IsFiltered))]
    public void ClearFiltering() => AmountForSharerID = LineItem.DinerID.none;
    #endregion
    #region Hints
    public bool ShowLineItemsHint
    {
        get;

        set => SetProperty(ref field, value, () => App.Settings.ShowLineItemsHint = value);
    } = false;

    public bool ShowTotalsHint
    {
        get;

        set => SetProperty(ref field, value, () => App.Settings.ShowTotalsHint = value);
    } = false;
    #endregion
    #region Meal Manipulation

    [RelayCommand]
    private async Task SaveMealNow()
    {
        if (Meal.CurrentMeal.IsDefault)
            await Utilities.DisplayAlertAsync("Default Bill", "You cannot save the default bill. Modify it and try again.", "ok");
        else
        {
            await Meal.CurrentMeal.SaveSnapshotAsync();
            await App.GoToAsync(Routes.MealListByAgePage);
        }
    }
    #endregion
    #region Handling Defaults
    public bool IsDefault => Meal.CurrentMeal.IsDefault;
    public bool IsDefaultTipRate => App.Settings.DefaultTipRate == (int)(Meal.CurrentMeal.TipRate * 100);
    public bool IsDefaultTaxRate => App.Settings.DefaultTaxRate == Meal.CurrentMeal.TaxRate;
    public bool IsDefaultTipOnTax => App.Settings.DefaultTipOnTax == Meal.CurrentMeal.TipOnTax;
    public bool IsDefaultCouponAfterTax => App.Settings.DefaultTaxOnCoupon == Meal.CurrentMeal.IsCouponAfterTax;
    public bool IsDefaultTip => IsDefaultTipOnTax && IsDefaultTipRate;
    public bool IsDefaultTax => IsDefaultTaxRate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDefaultTipRate))]
    [NotifyPropertyChangedFor(nameof(IsDefaultTip))]
    public partial decimal DefaultTipRatePercentage { get; set; }
    partial void OnDefaultTipRatePercentageChanged(decimal value) => App.Settings.DefaultTipRate = (int)value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDefaultTaxRate))]
    [NotifyPropertyChangedFor(nameof(IsDefaultTax))]
    public partial decimal DefaultTaxRatePercentage { get; set; }
    partial void OnDefaultTaxRatePercentageChanged(decimal value) => App.Settings.DefaultTaxRate = (double)value / 100;

    public bool DefaultTipOnTax
    {
        get => App.Settings.DefaultTipOnTax;
        set
        {
            if (App.Settings.DefaultTipOnTax != value)
            {
                App.Settings.DefaultTipOnTax = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDefaultTipOnTax));
                OnPropertyChanged(nameof(IsDefaultTip));
            }
        }
    }
    public bool DefaultTaxOnCoupon
    {
        get => App.Settings.DefaultTaxOnCoupon;
        set
        {
            if (App.Settings.DefaultTaxOnCoupon != value)
            {
                App.Settings.DefaultTaxOnCoupon = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDefaultCouponAfterTax));
                OnPropertyChanged(nameof(IsDefaultTax));
            }
        }
    }
    #endregion
}
