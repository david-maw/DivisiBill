using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Models;
using DivisiBill.Services;
using System.Collections.ObjectModel;

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
        LineItems = GetLineItems();
    }
    ~MealViewModel()
    {
        Meal.CurrentMeal.PropertyChanged -= CurrentMeal_PropertyChanged;
    }
    public void LoadLineItem()
    {
        LoadLineItemAmountString();
        LoadLineItemNameString();
    }
    public void UnloadLineItem()
    {
        UnloadLineItemAmountString();
        UnloadLineItemNameString();
    }
    public void LoadSettings() => LoadDefaultTaxRateString();
    public void UnloadSettings() => UnloadDefaultTaxRateString();
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
            OnPropertyChanged(nameof(ApproximateAge));
        }
        else if (e.PropertyName.Equals(nameof(Meal.LastChangeTime)))
        {
            OnPropertyChanged(nameof(ApproximateChangeAge));
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
    }
    #endregion
    #region Object Independent
    [RelayCommand]
    public async Task PushVenueList() => await App.PushAsync(Routes.VenueListByNamePage);

    [RelayCommand]
    public async Task PushProperties() => await App.PushAsync(Routes.PropertiesPage);
    #endregion
    #region PersonCost
    private readonly Stack<SavedCost> deletedCosts = new();
    private bool InsertCost(PersonCost pc)
    {
        int endInx = Costs.Count - 1; // Last element
        if (endInx + 1 >= LineItem.maxSharers)
            return false; // The list is already full

        if (endInx < 0 // costs list is empty, the merge is trivial
            || (pc.DinerIndex == endInx + 1)) // the one after the last element, so there's an empty slot
        {
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

    [RelayCommand]
    public void UndeleteCost()
    {
        if (IsAnyDeletedCost)
        {
            SavedCost sc = deletedCosts.Pop();
            InsertCost(sc.PersonCost);
            foreach (var si in sc.ShareInfoList)
                si.LineItem.SetShares(sc.PersonCost.DinerID, si.Shares);
            IsAnyDeletedCost = deletedCosts.Any();
            DistributeCostsIfNeeded();
        }
    }

    [RelayCommand]
    private void UndeleteAllCosts()
    {
        if (IsAnyDeletedCost)
        {
            while (deletedCosts.Any())
                UndeleteCost();
        }
    }

    [RelayCommand]
    public void ForgetDeletedCosts()
    {
        deletedCosts.Clear();
        IsAnyDeletedCost = false;
    }

    [RelayCommand]
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
    private PersonCost lastCostSelectedByMe = null;
#endif

    [RelayCommand]
    public void SelectCost(PersonCost pc)
    {
#if WINDOWS
        // Unfortunately Windows selects any new item before calling this code
        // probably related to https://github.com/dotnet/maui/issues/5446
        // This kludge works around that as long as you only use this method for selection
        if (pc == lastCostSelectedByMe)
        {
            SelectedCost = null;
            lastCostSelectedByMe = null;
        }
        else
        {
            SelectedCost = pc;
            lastCostSelectedByMe = pc;
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
            var navigationParameter = new ShellNavigationQueryParameters
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
        await Utilities.ShowPayments(new PaymentsViewModel(SubTotal + Tax - CouponAmountAfterTax, RoundedAmount, pc?.Nickname, pc is null ? 0 : RoundedAmount - Math.Round(pc.Amount), UnallocatedAmount));
    }

    [RelayCommand]
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
        SavedCost sc = new() { PersonCost = pc };
        foreach (var li in LineItems.Where((li) => li.SharedBy[pc.DinerIndex]))
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
    private void PersonCostRenumber(PersonCost pcToChange, LineItem.DinerID newUnusedDinerID)
    {
        // Validity check - ensure the new ID is unused
        if (null != Costs.FirstOrDefault(pc => pc.DinerID == newUnusedDinerID))
            return;
        LineItem.DinerID oldDinerID = pcToChange.DinerID;
        pcToChange.DinerID = newUnusedDinerID; // Important to do this first
        foreach (var li in LineItems.Where(li => li.GetShares(oldDinerID) > 0))
            li.TransferShares(newSharerID: newUnusedDinerID, oldSharerID: oldDinerID);
    }
    public void CostListResequence()
    {
        LineItem.DinerID desiredID = LineItem.DinerID.first;
        try
        {
            foreach (var pc in Costs.ToList())
            {
                if (pc.DinerID != desiredID)
                    PersonCostRenumber(pc, desiredID);
                desiredID++;
            }
        }
        catch (Exception ex)
        {
            Utilities.DebugMsg("In MealViewModel.CostListResequence, exception: " + ex);
        }
    }
    #endregion
    #region LineItem
    #region Data Entry
    #region Item Selection

    [ObservableProperty]
    public partial LineItem SelectedLineItem { get; set; }

    partial void OnSelectedLineItemChanging(LineItem oldValue, LineItem newValue)
    {
        if (oldValue is not null && IsValidLineItemAmountString)
            UnloadLineItem();
    }
    partial void OnSelectedLineItemChanged(LineItem value)
    {
        if (value is not null)
            LoadLineItem();
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
        li ??= new();
        LineItems.InsertBefore(SelectedLineItem, li);
        if (IsFiltered)
        {
            if (li.GetShares(AmountForSharerID) < 1)
                li.SetShares(AmountForSharerID, 1);
            Meal.CurrentMeal.LineItems.InsertBefore(SelectedLineItem, li); // because the one in LineItems is temporary.
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

    [RelayCommand]
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
    private void LoadLineItemAmountString() => LineItemAmountString = string.Format("{0:0.00}", SelectedLineItem.Amount);

    [RelayCommand]
    private void UnloadLineItemAmountString()
    {
        if (SelectedLineItem is not null && IsValidLineItemAmountString)
            SelectedLineItem.Amount = decimal.Parse(LineItemAmountString);
    }
    [RelayCommand]
    private void CompletedLineItemAmountString()
    {
        if (SelectedLineItem is null)
            return; // nothing to do
        // Store the value if it is valid
        if (IsValidLineItemAmountString)
            SelectedLineItem.Amount = decimal.Parse(LineItemAmountString);
        // We only move to the next item on the desktop because the soft keyboard on a phone takes up so much space that it obscures the item list
        if (DeviceInfo.Current.Idiom == DeviceIdiom.Desktop)
        {
            // Select the next item in the list or switch to the first one if this is the last one
            SelectedLineItem = LineItems.Alternate(SelectedLineItem);
        }
    }

    [ObservableProperty]
    public partial string LineItemAmountString { get; set; }
    #region ItemName
    public bool IsValidLineItemAmountString { get; set; } = false;
    #endregion
    private void LoadLineItemNameString() => LineItemNameString = SelectedLineItem.ItemName;

    [RelayCommand]
    private void UnloadLineItemNameString()
    {
        if (SelectedLineItem is not null)
            SelectedLineItem.ItemName = LineItemNameString;
    }

    [ObservableProperty]
    public partial string LineItemNameString { get; set; }
    #endregion
    #endregion
    #region Delete and UnDelete
    private readonly Stack<LineItem> deletedLineItems = new();
    public LineItem DeleteItem(LineItem li)
    {
        LineItem alternate = LineItems.Alternate(li);
        if (IsNotFiltered)
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
            foreach (var li in LineItems.Reverse())
                deletedLineItems.Push(li);
            LineItems.Clear();
            IsAnyDeletedLineItem = true;
        }
    }

    [RelayCommand]
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
    public void LineItemDeselected(LineItem li)
    {
        if (IsFiltered && li.GetShares(AmountForSharerID) < 1)
            LineItems.Remove(li);
    }

    [RelayCommand]
    private void UndeleteLineItem()
    {
        if (IsAnyDeletedLineItem)
        {
            LineItem li = deletedLineItems.Pop();
            IsAnyDeletedLineItem = deletedLineItems.Any();
            AddLineItem(li);
        }
    }

    [RelayCommand]
    private void UndeleteAllLineItems()
    {
        if (IsAnyDeletedLineItem)
        {
            LineItem firstUndeletedItem = deletedLineItems.Peek();
            while (deletedLineItems.Any())
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
                case "Up": if (LastVisibleItemIndex < lastItemIndex) ScrollLineItemsTo(LastVisibleItemIndex, false); break;
                case "Down": if (FirstVisibleItemIndex > 0) ScrollLineItemsTo(FirstVisibleItemIndex, true); break;
                case "End": if (LastVisibleItemIndex < lastItemIndex) ScrollLineItemsTo(lastItemIndex, false); break;
                case "Start": if (FirstVisibleItemIndex > 0) ScrollLineItemsTo(0, true); break;
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
                changeType = li.TotalSharers == 0 ? ChangeType.Proportional : ChangeType.Clear;
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

    [RelayCommand]
    public void ChangeSharing(object param)
    {
        if (param is string changeTypeString && Enum.TryParse(changeTypeString, out ChangeType changeType))
            ChangeSharing(SelectedOrFirstLineItem, changeType);
        else if (param is LineItem li)
            ChangeSharing(li, ChangeType.Cycle);
    }
    public event Action<LineItem> SharingChanged;

    [RelayCommand]
    public async Task GoToTotals() => await App.GoToAsync(Routes.TotalsPage);
    #endregion
    #endregion
    #region Totals, meal amounts and properties
    private decimal VisiblePositive => LineItems.Where(l => l.FilteredAmount > 0 && !l.Comped).Sum(l => l.FilteredAmount);

    private decimal VisibleNegative => -LineItems.Where(l => l.FilteredAmount < 0).Sum(l => l.FilteredAmount);
    public decimal SubTotal => Meal.CurrentMeal.SubTotal;
    private void SetFilteredSubtotal() => FilteredSubTotal = Math.Max(0, IsFiltered ? VisiblePositive - (IsCouponAfterTax ? 0 : VisibleNegative) : 0);
    public decimal FilteredSubTotal { get; private set => SetProperty(ref field, value); }
    public decimal TotalAmount => Meal.CurrentMeal.TotalAmount;
    public decimal RoundedAmount => Meal.CurrentMeal.RoundedAmount;
    public bool IsAnyUnallocated => Meal.CurrentMeal.UnallocatedAmount != 0;
    public decimal UnallocatedAmount => Meal.CurrentMeal.UnallocatedAmount;
    public Venue CurrentVenue => Venue.FindVenueByName(Meal.CurrentMeal.VenueName);
    public string ApproximateAge => Meal.CurrentMeal.ApproximateAge;
    public string ApproximateChangeAge => Utilities.ApproximateAge(LastChangeTime);
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
    public decimal CouponAmountAfterTax => Meal.CurrentMeal.CouponAmountAfterTax;
    private void SetFilteredCouponAmountAfterTax() => FilteredCouponAmountAfterTax = IsFiltered && IsCouponAfterTax ? VisibleNegative : 0;
    public decimal FilteredCouponAmountAfterTax { get; private set => SetProperty(ref field, value); }
    public decimal ScannedSubTotal => Meal.CurrentMeal.ScannedSubTotal;
    public decimal ScannedTax => Meal.CurrentMeal.ScannedTax;
    #endregion    
    #region Filtering for a sharer
    private void SetFilteredBlockTotals()
    {
        SetFilteredSubtotal();
        SetFilteredCouponAmountAfterTax();
        OnPropertyChanged(nameof(CouponAmountAfterTax)); // because this may need to be changed
    }
    public LineItem.DinerID AmountForSharerID
    {
        get => Meal.CurrentMeal.AmountForSharerID;
        set
        {
            if (Meal.CurrentMeal.AmountForSharerID != value)
            {
                Meal.CurrentMeal.AmountForSharerID = value;
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
    public bool IsNotFiltered => AmountForSharerID == LineItem.DinerID.none;
    public string FilteredSharerName => IsFiltered ? FilteredSharer.Nickname : string.Empty;
    private PersonCost FilteredSharer => IsFiltered ? Costs.Where((pc) => pc.DinerID == AmountForSharerID).FirstOrDefault() : null;

    private LineItem previousFilteredLineItem = null;
    [RelayCommand]
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

    [RelayCommand]
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
    public int DefaultTipRate
    {
        get => App.Settings.DefaultTipRate;
        set
        {
            if (App.Settings.DefaultTipRate != value)
            {
                App.Settings.DefaultTipRate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDefaultTipRate));
                OnPropertyChanged(nameof(IsDefaultTip));
            }
        }
    }
    public double DefaultTaxRate
    {
        get => App.Settings.DefaultTaxRate;
        set
        {
            if (App.Settings.DefaultTaxRate != value)
            {
                App.Settings.DefaultTaxRate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDefaultTaxRate));
                OnPropertyChanged(nameof(IsDefaultTax));
                OnPropertyChanged(nameof(DefaultTaxRateString));
            }
        }
    }

    [ObservableProperty]
    public partial bool IsDefaultTaxRateStringValid { get; set; }

    [ObservableProperty]
    public partial string DefaultTaxRateString { get; set; }
    private void LoadDefaultTaxRateString() => DefaultTaxRateString = string.Format("{0:0.00}", DefaultTaxRate * 100);

    [RelayCommand]
    private void UnloadDefaultTaxRateString()
    {
        if (IsDefaultTaxRateStringValid)
        {
            DefaultTaxRate = double.Parse(DefaultTaxRateString) / 100;
            LoadDefaultTaxRateString();
        }
    }
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
