using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace DivisiBill.Models;

/// <summary>
/// Represents a meal, including its associated line items and the costs assigned to each person.
/// </summary>
/// <remarks>This portion of the Meal class provides functionality for managing the list of items (such as dishes or expenses) and
/// the allocation of costs among participants (see <see cref="PersonCost"/> aka diners). It supports operations such as 
/// clearing and restoring line items or costs,
/// assigning and removing diners, and calculating proportional shares. This class is designed to facilitate scenarios
/// where expenses need to be split among multiple people, such as in group dining or shared billing
/// applications.</remarks>
public partial class Meal
{
    [ObservableProperty]
    public partial ObservableCollection<PersonCost> Costs { get; set; } = [];
    partial void OnCostsChanged(ObservableCollection<PersonCost> oldValue, ObservableCollection<PersonCost> newValue) => MarkAsChanged();

    [ObservableProperty]
    public partial ObservableCollection<LineItem> LineItems { get; set; } = [];
    partial void OnLineItemsChanged(ObservableCollection<LineItem> value) => MarkAsChanged();

    #region Clearing and restoring the list of items
    private readonly List<LineItem> savedLineItems;
    private int savedNextItemNumber;

    /// <summary>
    /// Indicates whether the list of line items can be cleared.
    /// </summary>
    public bool CanClearLineItems => (LineItems.Count > 1) || ((LineItems.Count > 0) && (LineItems[0].Amount > 0));

    /// <summary>
    /// Indicates whether the last clear operation on line items can be undone.
    /// </summary>
    public bool CanUndoClearLineItems => savedLineItems.Count != 0;

    /// <summary>
    /// Restores the line items to their state before the last clear operation, if possible.
    /// </summary>
    public void UndoClearLineItems()
    {
        if (savedLineItems.Count != 0)
        {
            LineItems.Clear();
            foreach (var item in savedLineItems)
                LineItems.Add(item);
            savedLineItems.Clear();
            LineItem.NextItemNumber = savedNextItemNumber;
            // Now make sure all the diners still exist
            var dinerIndexValid = new bool[LineItem.maxSharers];
            foreach (var item in Costs)
                dinerIndexValid[item.DinerIndex] = true;
            foreach (var item in LineItems)
            {
                for (int i = 0; i < LineItem.maxSharers; i++)
                {
                    if (item.SharedBy[i] && !dinerIndexValid[i])
                        item.SharedBy[i] = false;
                }
            }
        }
    }

    /// <summary>
    /// Clears the list of line items, saving the current state so it can be restored with <see cref="UndoClearLineItems"/>.
    /// </summary>
    /// <returns>True if the line items were cleared; otherwise, false.</returns>
    public bool ClearLineItems()
    {
        if (LineItems.Count > 1 || (LineItems.Count == 1 && LineItems[0].Amount != 0))
        {
            savedLineItems.Clear();
            foreach (var item in LineItems)
                savedLineItems.Add(item);
            LineItems.Clear();
            savedNextItemNumber = LineItem.NextItemNumber;
            LineItem.NextItemNumber = 1;
            return true;
        }
        return false;
    }
    #endregion

    #region Costs (list of PersonCost)
    #region Clearing and restoring the list of diners with costs
    private List<PersonCost> savedCosts;

    /// <summary>
    /// Indicates whether the last clear operation on costs can be undone.
    /// </summary>
    public bool CanUndoCosts => savedCosts is not null && savedCosts.Count > 0;

    /// <summary>
    /// Restores the list of costs to their state before the last clear operation, if possible.
    /// </summary>
    public void UndoCosts()
    {
        // The costs list is, by design, stored in DinerIndex order and consequently, so is the savedCosts list
        // So, iterate through savedCosts from last to first and you can make one pass through Costs
        // replacing or inserting items as needed

        if (CanUndoCosts)
        {
            int costInx = Costs.Count - 1; // Last element

            if (costInx < 0)
            {
                // costs list is empty, the merge is trivial
                foreach (var pc in savedCosts)
                    Costs.Add(pc);
            }
            else
            {
                for (int savedCostInx = savedCosts.Count - 1; savedCostInx >= 0; savedCostInx--)
                {
                    var pc = savedCosts[savedCostInx];
                    while ((costInx >= 0) && (Costs[costInx].DinerID > pc.DinerID))
                    {
                        costInx--;
                    }
                    if ((costInx >= 0) && (Costs[costInx].DinerID == pc.DinerID))
                    {
                        // Oh dear, the CostIndex has been reused, so do not replace this one if it is in use
                        var newPersonCost = Costs[costInx];
                        if (newPersonCost.Amount == 0) // If the amount is zero, perhaps no items refer to it
                        {
                            bool shared = false;
                            foreach (var item in LineItems)
                            {
                                if (item.SharedBy[newPersonCost.DinerIndex])
                                {
                                    shared = true;
                                    break;
                                }
                            }
                            if (!shared) // nobody is sharing this one, add it to the list of ones we can remove
                            {
                                Costs.RemoveAt(costInx); // Remove the diner with the same DinerIndex
                                Costs.Insert(costInx, pc); // put the new diner in the same place
                            }
                        }
                    }
                    else
                        Costs.Insert(costInx + 1, pc); // Insert the new diner after the one with a smaller DinerIndex
                }
            }
            // Now throw away the saved ones
            savedCosts.Clear();
        }
    }

    /// <summary>
    /// Clears costs for diners that have no associated line items, saving the removed costs so they can be restored.
    /// </summary>
    public void ClearCosts()
    {
        var newSavedCosts = new List<PersonCost>();
        foreach (var pc in Costs)
        {
            if (pc.Amount == 0) // If the amount is zero, perhaps no items refer to it
            {
                bool shared = false;
                foreach (var item in LineItems)
                {
                    if (item.SharedBy[pc.DinerIndex])
                    {
                        shared = true;
                        break;
                    }
                }
                if (!shared) // nobody is sharing this one, add it to the list of ones we can remove
                    newSavedCosts.Add(pc);
            }
        }
        if (newSavedCosts.Count > 0)
        {
            foreach (var pc in newSavedCosts)
                Costs.Remove(pc);
            savedCosts = newSavedCosts;
        }
    }

    /// <summary>
    /// Gets the next <see cref="PersonCost"/> in the list after the specified one, or the first if the current one is null.
    /// </summary>
    public PersonCost GetNextPersonCost(PersonCost currentPc) => currentPc is null ? Costs.FirstOrDefault() : Costs.SkipWhile(pc => pc != currentPc).Skip(1).FirstOrDefault();
    #endregion
    #region Manipulating cost list
    /// <summary>
    /// Calculates a set of values proportionate to the values passed in the amounts array, these values are is determined heuristically
    /// meaning a more accurate approximation may exist, but this function will stop when it finds one that is "good enough".
    /// 
    /// Negative amounts are basically ignored.
    /// </summary>
    /// <param name="amounts">Array of individual amounts assigned to each array index</param>
    /// <returns>An array of share values from 0 to 9 to approximate the proportions in the amounts array</returns>
    public static byte[] CostsToShares(decimal[] amounts)
    {
        double totalAmount = 0, maxAmount = 0;

        foreach (var a in amounts)
        {
            if (a > 0)
            {
                totalAmount += (double)a;
                maxAmount = Math.Max(maxAmount, (double)a);
            }
        }

        byte[] bestShares = new byte[amounts.Length];
        double bestDifference = double.MaxValue;
        foreach (int maxShares in new List<int>() { 8, 9, 7, 6, 5 }) // 4 and 2 are equivalent to 8, 3 is equivalent to 6
        {
            byte[] shares = new byte[amounts.Length];
            double difference = 0;
            double shareAmount = maxAmount / maxShares; // So maxShares are sufficient to represent the maximum cost
                                                        // This means nobody will have more than maxShares
            for (int i = 0; i < amounts.Length; i++)
            {
                if (amounts[i] > 0)
                {
                    shares[i] = (byte)Math.Round((double)amounts[i] / shareAmount);
                    difference += Math.Pow((double)(shareAmount * shares[i] - (double)amounts[i]), 2);
                }
            }
            if (difference < bestDifference) // This new value is the best so far
            {
                bestDifference = difference;
                bestShares = shares;
            }

            if (difference < (totalAmount / 10000)) // This is our definition of "good enough"
                break;
        }

        // At this point we have our best guess share proportions, but not necessarily in the simplest form, so fix that
        SimplifyShares(bestShares);

        return bestShares;
    }

    /// <summary>
    /// Takes a list of shares in a byte array and removes any common factors so they are in as simple a form as possible.
    /// For example 8:4:2 would be simplified to 4:2:1.
    /// </summary>
    /// <param name="shares">An array of individual share values (positive integers or zero)</param>
    public static void SimplifyShares(byte[] shares)
    {
        static int gcd(int a, int b) // Greatest Common Divisor - look it up 
=> a == 0 ? b : gcd(b % a, a);

        int GCD = 0;

        // Find the GCD
        foreach (byte i in shares.Where(i => i > 0))
            GCD = gcd(GCD, i);
        // Divide each share amount by it, to get them in the lowest common denominator
        for (int i = 0; i < shares.Length; i++)
        {
            if (shares[i] > 0)
            {
                shares[i] = (byte)(shares[i] / GCD);
            }
        }
    }

    /// <summary>
    /// Take the PersonCost in <paramref name="pc"/> and give it the new <paramref name="newDinerID"/> and place the
    /// former data cost item for the new DinerID in the old DinerID slot. 
    /// </summary>
    public void AssignDinerID(PersonCost pc, LineItem.DinerID newDinerID)
    {
        LineItem.DinerID oldDinerID = pc.DinerID;
        // Iterate through the items, moving the sharers from old to new entry
        foreach (var costItem in LineItems)
            costItem.SwapSharerID(newDinerID, oldDinerID);
        // Find if a PersonCost used to use this DinerID and if so give it the ID from this one
        PersonCost previousPersonCost = Costs.FirstOrDefault(item => item.DinerID == newDinerID);
        previousPersonCost?.DinerID = pc.DinerID;
        pc.DinerID = newDinerID;
    }

    /// <summary>
    /// Remove a specific <see cref="PersonCost"/> from the list and clear its shares from all line items.
    /// </summary>
    public void CostListDelete(PersonCost pc)
    {
        if (Costs.Count == 0)
            return;
        //Now remove that diner from any items
        LineItem.DinerID dinerID = pc.DinerID;
        // Costs is sorted by DinerID, so just look in the right place
        foreach (var item in LineItems)
        {
            if (item.GetShares(dinerID) > 0)
                item.SetShares(dinerID, 0);
        }
        // Now the diner has been removed from all items it is safe to delete
        Costs.Remove(pc);
        DistributeCosts();
    }

    /// <summary>
    /// Remove all diners with costs and deallocate all shares in line items.
    /// </summary>
    public void CostListDeleteAll()
    {
        if (Costs.Count == 0)
            return;
        foreach (var li in LineItems)
            li.DeallocateShares();
        Costs.Clear();
    }

    /// <summary>
    /// Adds a new <see cref="PersonCost"/> for the specified <see cref="Person"/> if possible.
    /// </summary>
    /// <param name="p">The person to add.</param>
    /// <returns>The created <see cref="PersonCost"/>, or null if the person already exists or the maximum number of sharers is reached.</returns>
    public PersonCost CostListAdd(Person p)
    {
        if (Frozen) // If this is an untouched bill it might need reordering to eliminate gaps in cost numbers
            CostListResequence();
        if (Costs.Count >= LineItem.maxSharers)
            return null;
        foreach (var item in CurrentMeal.Costs)
        {
            if (item.Diner == p)
                return null;
        }
        LineItem.DinerID availDinerID = (LineItem.DinerID)((int)LineItem.DinerID.first + Costs.Count);
        // Allocate a new item, populate it, and add it to the list
        var pc = new PersonCost() { DinerID = availDinerID, Diner = p };
        Costs.Insert(pc.DinerIndex, pc);
        return pc;
    }

    /// <summary>
    /// Reassigns the DinerID of a specified PersonCost to a new, unused DinerID and updates all related LineItems to
    /// reflect the change.
    /// </summary>
    /// <remarks>If the specified new DinerID is already in use, no changes are made. All LineItems that
    /// reference the old DinerID will have their shares transferred to the new DinerID.</remarks>
    /// <param name="pcToChange">The PersonCost instance whose DinerID will be updated.</param>
    /// <param name="newUnusedDinerID">The new DinerID to assign to the PersonCost. Must not already be in use by another PersonCost.</param>
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

    /// <summary>
    /// Resequences the DinerID values of all items in the cost list to ensure they are in sequential order starting
    /// from the first DinerID.
    /// </summary>
    /// <remarks>This method updates the DinerID of each cost item so that they are ordered consecutively. It
    /// is typically used to maintain consistency after items have been added or removed from the cost list.</remarks>
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
            Services.Utilities.DebugMsg("In Meal.CostListResequence, exception: " + ex);
        }
    }
    #endregion 
    #endregion
}