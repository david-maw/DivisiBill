using CommunityToolkit.Mvvm.ComponentModel;
using DivisiBill.Services;
using System.Diagnostics;
using System.Xml.Serialization;

namespace DivisiBill.Models;

public partial class Meal
{
    #region Distribution State Properties
    /// <summary>
    /// Indicates whether all amounts (items, tax, tip, and discounts) have been distributed among participants.
    /// </summary>
    [XmlIgnore]
    public bool IsDistributed { get; set; } = false;

    /// <summary>
    /// This is the 'smush' left over when all the costs have been allocated to participants. It is supposed to be a few cents 
    /// at most caused by inevitable rounding errors as we share items amongst participants. We generally only expose it to the
    /// user when it is too large, indicating there is some sort of calculation problem. 
    /// </summary>
    [XmlIgnore]
    public decimal RoundingErrorAmount
    {
        get;
        private set
        {
            SetProperty(ref field, value);
            IsUnsharedAmountSignificant = value != 0 && !IsAnyUnallocated && value * 100 > (Costs.Count(pc => pc.Amount > 0) + 1);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the unshared amount is significant and may indicate a calculation issue.
    /// </summary>
    /// <remarks>A significant unshared amount typically suggests that the result of a calculation may be
    /// incorrect or requires further investigation. This property can be used to detect and handle potential data
    /// inconsistencies but it is mainly used to alert the user that something is up because historically this was a 
    /// problem</remarks>

    [ObservableProperty]
    [XmlIgnore]    // The UnsharedAmount is too large indicating there is some sort of calculation problem.
    public partial bool IsUnsharedAmountSignificant { get; private set; }

    /// <summary>
    /// The amount not allocated to any participant (so the sum of all the unallocated items). It's faintly possible
    /// that a negative unallocated amount could offset a positive one but that's so unlikely it's not worth coding for.
    /// Note that this is different from the Unshared amount.
    /// </summary>
    [ObservableProperty]
    [XmlIgnore]
    public partial decimal UnallocatedAmount { get; private set; }
    partial void OnUnallocatedAmountChanged(decimal value) => IsDistributed = false;

    /// <summary>
    /// Calculates the total amount that has not been allocated to any participant based on the current line items.
    /// </summary>
    /// <remarks>
    /// Iterates through all line items and sums the absolute value of amounts for items that are not allocated
    /// to any participant. If no unallocated amounts are found, it then checks for the edge case where discounts
    /// exceed the total costs, in which case the unused discount is returned as a negative value.
    /// </remarks>
    /// <returns>The total unallocated amount for the meal, including any excess discount as a negative value.</returns>
    public decimal GetUnallocatedAmount()
    {
        decimal d = 0;
        foreach (LineItem item in LineItems)
        {
            bool isAllocated = false;
            foreach (bool payee in item.SharedBy)
            {
                if (payee)
                {
                    isAllocated = true;
                    break;
                }
            }
            if (!isAllocated)
                d += Math.Abs(item.Amount);
        }
        if (d == 0) // No amounts unallocated
        {
            // Check for unusual case where discounts exceed costs
            decimal UnusedDiscount = GetOrderAmount() - GetRawCouponAmount() / (1M + (IsCouponAfterTax ? (decimal)TaxRate : 0));
            if (UnusedDiscount < 0)
                d = UnusedDiscount;
        }
        return d;
    }
    #endregion
    /// <summary>
    /// <para>Walk through all the Cost items (the participants) and allocate the appropriate share of the costs to each participant.</para>
    /// <para>Do this by allocating the cost of each item to the sharers for that item, then sharing the tax and tip amounts in proportion
    /// to the item based amount.</para>
    /// <para>This is really the core functionality of the program, distributing item costs, tax and tip
    /// between participants. It's pretty easy in the "normal" case of just a list of items shared out with tax and tip</para>
    /// <para>Some of the cases to handle include:</para>
    /// <list type="bullet">
    ///    <item><description>Tip on tax or not.</description></item>
    ///    <item><description>Taxable Coupons or not.</description></item>
    ///    <item><description>Coupons amount exceeds overall amount spent.</description></item>
    ///    <item><description>One or more participant coupon amounts exceed participant spend.</description></item>
    ///    <item><description>No participant spent anything.</description></item>
    ///    <item><description>Unallocated amount includes unallocated coupons.</description></item>
    /// </list>
    /// <para>Taxable discounts (which are rare) are handled by calculating what the discount before tax would have been and using 
    /// that in the calculations so we don't have to distribute it separately.</para>
    /// <para>Tip amounts are not affected by discounts (comped items or coupons)</para>
    /// <para>For any unused discount or error due to rounding we share it between participants but try and keep identical payments identical.</para>
    /// </summary>
    public void DistributeCosts(bool report = true)
    {
        #region Initial Evaluation and Tests
        if (Costs.Count == 0)
            return; // There's nobody to share with 
        #endregion
        #region Initialization
        var sharers = new PersonCost[LineItem.maxSharers];

        if (LineItems.Count == 0)
        {
            // As there are no people to share amongst we've done all that is necessary, just zero out a few things and exit
            RoundedAmount = 0;
            RoundingErrorAmount = 0;
            foreach (PersonCost pc in Costs)
                pc.ClearAllAmounts();
            return;
        }
        #endregion
        #region Share out Items
        decimal unallocatedRunningTotal = 0;
        // Store a reference to each participants cost at their diner index 
        // in the sharers array to simplify the next step
        foreach (PersonCost personCost in Costs)
        {   // Note DinerIndex starts at 1
            sharers[personCost.DinerIndex] = personCost;
            personCost.ClearAllAmounts(); // Take this opportunity to clear out old data (even for irrelevant fields it simplifies debugging)
        }
        // Now step through all the line items, sharing out their cost
        foreach (LineItem item in LineItems)
        {
            // Figure out what each person pays toward that item
            decimal[] amounts = item.GetAmounts();
            bool isUnallocated = true;
            // Now go though the sharers, allocating an amount to each
            for (int i = 0; i < LineItem.maxSharers; i++)
            {
                decimal amount = amounts[i];
                if (amount != 0)
                {
                    isUnallocated = false;
                    PersonCost pc = sharers[i]; // Find the cost item for sharer sharerInx
                    if (pc is null)
                    {
                        // This is an invalid meal, kludge it
                        pc = new PersonCost() { Nickname = "Unknown" + (i + 1).ToString(), DinerID = (LineItem.DinerID)(i + 1) };
                        sharers[i] = pc;
                        Costs.Add(pc);
                    }
                    if (amount < 0)
                    {
                        pc.CouponAmount -= amount; // Amount is negative, so CouponAmount will be positive
                        // Notice that the comped flag is ignored on discounts
                    }
                    else if (item.Comped) // This item was comped, so the amount paid can be discounted
                    {
                        pc.CompedAmount += amount;
                        pc.OrderAmount += amount;
                    }
                    else // a simple share of item cost
                    {
                        pc.OrderAmount += amount;
                    }
                }
            } // end loop distributing shares
            if (isUnallocated)
                unallocatedRunningTotal += Math.Abs(item.Amount);
        }

        UnallocatedAmount = unallocatedRunningTotal;

        // At this point all the basic values derived from the list of items are accumulated in each PersonCost entry
        // this includes each participant's OrderAmount, CompedAmount and CouponAmount but not yet any tax, tip or final amounts.
        // It is possible that the discounts (CompedAmount + CouponAmount) exceed the total spent for some or all participants.

        // Calculate the discount for each participant, if coupons are to be applied after tax, scale the coupon amount to a corresponding discount before tax
        decimal totalChargedAmount = 0, totalCouponAmount = 0;
        foreach (PersonCost costItem in Costs)
        {
            costItem.PreTaxCouponAmount = costItem.CouponAmount / (1M + (IsCouponAfterTax ? (decimal)TaxRate : 0));
            costItem.Discount = costItem.CompedAmount + costItem.PreTaxCouponAmount;
            costItem.UnusedCouponAmount = costItem.Discount;
            totalCouponAmount += costItem.PreTaxCouponAmount;
            totalChargedAmount += costItem.ChargedAmount; // CouponAmount not included
        }

        // Basic derived items (PreTaxCouponAmount, Discount and UnusedCouponAmount) are now defined for each participant but again
        // no tax, tip or final amounts have been calculated yet and the discount may exceed the amount.

        // Create a handy list of participants who spent something
        var costsWithOrderAmount = Costs.Where(pc => pc.OrderAmount > 0).ToList();
        if (costsWithOrderAmount.Count == 0)
        {
            // Trivial case, nobody spent anything so there's nothing much to calculate, just guess at the rounded amount, mark as completed, and return
            RoundedAmount = GetRoundedAmount();
            IsDistributed = true;
            return;
        }
        #endregion
        #region Ensure the Coupon Amount Does not Exceed the Overall Cost
        // This is a rare case (it means a large coupon in comparison to the bill), but nevertheless, if it
        // does happen, most venues will not give you money back (if they did, you effectively have money, 
        // not a coupon). If excess does happen, prorate the individual coupons so the overall total amount ends up at zero
        decimal ExcessDiscount = Math.Max(0, totalCouponAmount - totalChargedAmount);
        if (ExcessDiscount > 0)
        {
            Tax = 0; // Because there will be no costs there can be no tax
            // Calculate the ratio by which to multiply each participant's coupon amount so the total equals the total cost
            decimal ratio = totalChargedAmount / totalCouponAmount;
            foreach (PersonCost costItem in Costs.Where(pc => pc.PreTaxCouponAmount > 0))
            {
                // Scale this participant's coupon share appropriately
                costItem.PreTaxCouponAmount *= ratio;
                // No coupon amount has been used yet, assign the initial amount
                costItem.UnusedCouponAmount = costItem.PreTaxCouponAmount;
                // figure out the maximum discount for this participant using the comped amount and prorated coupon amount 
                costItem.Discount = costItem.CompedAmount + costItem.PreTaxCouponAmount;
            }
        }
        #endregion
        #region Calculate Amount by Applying Discount (Coupon + Comp) Amounts to OrderAmount
        // In most bills the discount would have been completely consumed but...
        // There is an edge case where some people may have discounts which exceed their costs. However,
        // they are still on the hook for a tip, which may yet consume their remaining discount. Discounts
        // do not reduce the total tip amount but other participants will be putting cash in to cover the tip
        // so we'll reallocate any unused discount left after paying for a tip to other people, remember, the
        // discount has already been prorated so as not to exceed the sum of the paid (not comped) items.
        // We could be more methodical about this so as to distribute the extra discount according to
        // the shares specified by the user, but this is such an unlikely case it hardly seems worthwhile.

        // First, work through all the costs that are in use consuming as much of the unused coupons
        // as possible noting what cash remains so it can be consumed by tip amounts later if possible.

        // The amount of discount we have not yet used across all the participants
        decimal remainingUnusedCouponAmount = 0;
        // Sum of all the amounts so far calculated
        totalChargedAmount = 0;
        foreach (PersonCost costItem in Costs)
        {
            if (costItem.Discount <= costItem.OrderAmount)
            {
                // The normal case, where the discount is smaller than the participant's total cost
                costItem.Amount = costItem.OrderAmount - costItem.Discount;
                totalChargedAmount += costItem.Amount;
                costItem.UnusedCouponAmount = 0; // We have used up all of this participant's coupon share
            }
            else
            {
                // The unusual case where the discount exceeds the cost 
                costItem.Amount = 0;
                // amountSum is unchanged because we would have added zero to it
                costItem.UnusedCouponAmount -= costItem.ChargedAmount;
                remainingUnusedCouponAmount += costItem.UnusedCouponAmount;
            }
        }
        // In the normal case when we get to here Amount contains the taxable amount for each participant
        // However, in the odd case where some participants had more discount than cost, there may be
        // some unused discount left over to be applied against other participants cost
        if (remainingUnusedCouponAmount > 0)
        {
            decimal discountPerUnit = remainingUnusedCouponAmount / Costs.Sum(pc => pc.Amount);
            foreach (PersonCost costItem in Costs)
            {
                if (costItem.Amount > 0)
                    costItem.Amount -= costItem.Amount * discountPerUnit;
                costItem.UnusedCouponAmount = 0; // We used all we can so record that
            }
            remainingUnusedCouponAmount = 0; // Because we just consumed it all
        }
        #endregion
        #region Apply Proportional Tax, Tip, and Discount To Each Cost 
        // Now step through the totals for each person that spent something and add in tax and tip.
        // Coupon amounts may, or may not be applied before tax, it is an option on each bill. In the rare case
        // where coupons are after-tax a calculated equivalent before-tax amount will have been applied.
        // Tax is shared in proportion to what was actually taxable (so if discounts were taxable, they count)
        // Tip is shared based on what is actually spent, so just because an item was discounted or comped,
        // you still get to tip on it

        decimal modifiedTaxRate = Tax == 0 ? 0 : Tax / TaxedAmount; // identical to TaxRate unless TaxDelta is set
        decimal modifiedTipRate = Tip == 0 ? 0 : Tip / GetTipBasis(); // identical to TipRate unless TipDelta is set

        foreach (PersonCost costItem in costsWithOrderAmount) // So, just the people who bought things
        {
            decimal shareOfTax = costItem.Amount * modifiedTaxRate;
            // The tip is shared according to what each person spent
            decimal shareOfTip = (costItem.OrderAmount + (TipOnTax ? shareOfTax : 0)) * modifiedTipRate;

            // At this point we can make a first estimate of what this participant owes ignoring unused discounts and rounding
            costItem.Amount += shareOfTax + shareOfTip;
            // In rare cases this participant will have some unused discount, if so, try and use it
            if (costItem.UnusedCouponAmount == 0)
            {
                // The normal case - we already consumed the discount for each participant, so there is nothing to do
            }
            else if (costItem.UnusedCouponAmount <= costItem.Amount)
            {
                // There's some unused discount, but it is less than the Amount for the participant
                costItem.Amount -= costItem.UnusedCouponAmount;
                remainingUnusedCouponAmount -= costItem.UnusedCouponAmount;
                costItem.UnusedCouponAmount = 0;
            }
            else
            {
                // The really rare case where the unused discount exceeds the remaining amount, this means
                // a different participant will pay part of this participant's share using their unused discount 
                costItem.UnusedCouponAmount -= costItem.Amount;
                remainingUnusedCouponAmount -= costItem.Amount;
                costItem.Amount = 0;
            }
        }
        // In the normal case when we get here all tax, tip and discounts have been applied and the Amount for each
        // participant represents what they should actually pay except that it is not yet rounded. 
        #endregion
        #region Get Handy List and Debug Remaining Discount
        // At this point, everyone has used up as much of their share of the discount as possible so we have
        // to use up the remainder. We'll just add it to the rounding error down below and distribute them together.

        // Get a list of just the people who still owe money because they can consume discount
        var costsWithAmount = Costs.Where(pc => pc.Amount > 0).ToList();

        // The following check isn't necessarily reporting something wrong, but it is weird enough to be worth noting
        if (UnallocatedAmount == 0 && remainingUnusedCouponAmount != 0)
            Utilities.RecordMsg($"Excess discount {remainingUnusedCouponAmount:C} is unusual in {DebugDisplay}");
        #endregion
        #region Round all values
        // Until now we've been doing full accuracy calculations so as to minimize rounding errors
        // From this point onward, all the amounts are in exact dollars and cents so we have to handle them explicitly.
        foreach (PersonCost pc in costsWithOrderAmount)
            pc.RoundAllAmounts();
        SubTotal = totalChargedAmount = Math.Round(totalChargedAmount, 2);
        CouponAmountIfAfterTax = Math.Round(GetCouponAmountIfAfterTax(), 2);
        remainingUnusedCouponAmount = Math.Round(remainingUnusedCouponAmount, 2);
        totalCouponAmount = Math.Round(totalCouponAmount, 2);
        decimal roundingErrorLeft = Math.Round(GetTotalAmount() - costsWithAmount.Sum(pc => pc.Amount), 2); // The difference between the bill total and sum of individual amounts
        #endregion
        #region Verify That Any Rounding Error is Small
        // Make the original rounding error visible so the UI can present it as a dire warning if it is large
        if (UnallocatedAmount == 0)
            RoundingErrorAmount = roundingErrorLeft + remainingUnusedCouponAmount; // This produces the actual rounding error regardless of unused discount 
        else
            roundingErrorLeft = 0;
        /* At this point, there may be a few cents left over, caused by the difference between rounding individual totals 
         * after adding tax and tip and summing the results versus calculating the total, adding tax and tip, then rounding.
         * The difference is generally +/- one cent at most, but it could be as much as +/- one cent per person in theory and
         * for those odd bills where there is still some unused coupon amount it could be relatively large.
         * In the unusual case of large discount there may be more left but either way, we just share it out.
        */
        if (report && UnallocatedAmount == 0)
            Utilities.DebugAssert(Math.Abs(RoundingErrorAmount) <= (0.01m * Math.Max(1, costsWithAmount.Count)),
               $"in Meal.{nameof(DistributeCosts)}: {RoundingErrorAmount:C} unallocated after sharing costs in {DebugDisplay}");
        #endregion
        #region Share Out Any Rounding Error and, Rarely, Remaining Discount
        // Now ensure that if multiple participants had the same cost they pay the same amount because when what was purchased was the same but
        // the amounts owed are different, it tends to be noticeable so we try not to do that.
        // At this point roundingErrorLeft includes remainingUnusedCouponAmount
        if (Math.Abs(roundingErrorLeft) >= 0.01M)
        {
            // Group participants into lists with the same amount
            var amountClusters = costsWithAmount.Where(ci => ci.Amount > 0)
                .GroupBy(ci => ci.OrderAmount, (orderAmount, g) => new { OrderAmount = orderAmount, SameOrderAmountCount = g.Count(), CostsWithSameOrderAmount = g });

            if (Math.Abs(roundingErrorLeft) >= 0.02M)
            {
                // Step through each group with more than one member and see if there's enough to give all of them some 
                foreach (var cluster in amountClusters.Where(result => result.SameOrderAmountCount > 1))
                {
                    if (cluster.SameOrderAmountCount * 0.01M > Math.Abs(roundingErrorLeft))
                        continue; // Skip over groups with too many members to be able to share equally
                    decimal totalForCluster = cluster.CostsWithSameOrderAmount.Sum(costItem => costItem.Amount) + roundingErrorLeft;
                    decimal amountPerParticipant = Math.Truncate(totalForCluster * 100 / cluster.SameOrderAmountCount) / 100; // round down to the nearest penny
                    roundingErrorLeft = totalForCluster - amountPerParticipant * cluster.SameOrderAmountCount;
                    foreach (PersonCost costItem in cluster.CostsWithSameOrderAmount)
                        costItem.Amount = amountPerParticipant;
                    if (roundingErrorLeft == 0)
                        break; // All done
                }
            }
            if (Math.Abs(roundingErrorLeft) >= 0.005m) // sharing among clusters of identical orders didn't do it, try giving it to any solo participant 
            {
                PersonCost? ci = amountClusters.Where(result => result.SameOrderAmountCount == 1) // Just the ones with unique order amounts
                    .SelectMany(cluster => cluster.CostsWithSameOrderAmount) // Flatten the lists
                    .FirstOrDefault(ci => (ci.Amount + roundingErrorLeft) > 0); // now see if there's one that can handle the remainder 
                if (ci is not null)
                {
                    ci.Amount += roundingErrorLeft;
                    roundingErrorLeft = 0; // we consumed it 
                }
            }
        }
        if (roundingErrorLeft != 0) // As a last resort, just give it to the first participant that can handle it
        {
            // The extra is added(or subtracted from) the first non zero total it would not overwhelm.
            PersonCost? costItem = costsWithAmount.FirstOrDefault(ci => (ci.Amount + roundingErrorLeft) > 0);
            if (costItem is not null)
                costItem.Amount += roundingErrorLeft;
            else // We could not find any way to allocate remainingTotal should always be zero
                Utilities.DebugMsg($"In Meal.{nameof(DistributeCosts)}: unable to eliminate rounding error {roundingErrorLeft:C} in {DebugDisplay}");
        }
        RoundingErrorAmount = roundingErrorLeft; // Should be zero now, so make sure we tell the user
        #endregion
        #region Final Calculations and Cleanup
        // Now that all the costs have been distributed to individuals, recalculate the total amounts
        TotalAmount = Math.Round(GetTotalAmount(), 2);
        RoundedAmount = GetRoundedAmount();
        if (UnallocatedAmount == 0 && ExcessDiscount != 0)
            UnallocatedAmount = -ExcessDiscount; // To make it obvious to the user
        // And note that distribution is now accurate
        IsDistributed = true;
        #endregion
    }

    /// <summary>
    /// The old version of the DistributeCosts Method.
    /// </summary>
    public void DistributeCosts20230527()
    {
        static decimal[] GetAmounts(LineItem li)
        {
            decimal[] amounts = new decimal[LineItem.maxSharers];
            int centsLeft = (int)(100 * li.Amount); // Exact number of cents
            int howMany = li.TotalShares;
            if (howMany > 0)
            {
                // Figure out what each person pays toward that item
                int eachShare = centsLeft / howMany;
                // Now go though the sharers, allocating an amount to each
                for (int i = 0; (i < LineItem.maxSharers) && (howMany > 0); i++)
                {
                    if (li.SharedBy[i])
                    {
                        int shares = 1 + li.ExtraShares[i];
                        howMany -= shares;
                        decimal amount;
                        if (howMany > 0)
                        {
                            amount = (decimal)((double)(eachShare * shares) / 100);
                            centsLeft -= eachShare * shares;
                        }
                        else
                            amount = (decimal)((double)centsLeft / 100); // Last person gets the remainder
                        amounts[i] += amount;
                    }
                } // end loop distributing shares
            }
            return amounts;
        }

        UnallocatedAmount = GetUnallocatedAmount();
        if (Costs.Count == 0)
            return; // There's nobody to share with

        var sharers = new PersonCost[LineItem.maxSharers];

        // Store a reference to each participants cost at their diner index 
        // in the sharers array to simplify the next step
        foreach (PersonCost item in Costs)
        {   // Note DinerIndex starts at 1
            sharers[item.DinerIndex] = item;
            item.OrderAmount = 0;
            item.Amount = 0;
            item.Discount = 0;
            item.CompedAmount = 0;
        }
        if (LineItems.Count == 0)
        {
            // As there are no people to share amongst we've done all that is necessary, just zero out a few things and exit
            RoundedAmount = 0;
            RoundingErrorAmount = 0;
            return;
        }
        // Now step through all the line items, sharing out their cost
        foreach (LineItem item in LineItems)
        {
            // Figure out what each person pays toward that item
            decimal[] amounts = GetAmounts(item);
            // Now go though the sharers, allocating an amount to each
            for (int i = 0; i < LineItem.maxSharers; i++)
            {
                decimal amount = amounts[i];
                if (amount != 0)
                {
                    PersonCost pc = sharers[i]; // Find the cost item for sharer sharerInx
                    if (pc is null)
                    {
                        // This is an invalid meal, kludge it
                        pc = new PersonCost() { Nickname = "Unknown" + (i + 1).ToString(), DinerID = (LineItem.DinerID)(i + 1) };
                        sharers[i] = pc;
                        Costs.Add(pc);
                    }
                    if (item.Comped) // This item was comped, so the amount paid can be discounted
                    {
                        pc.CompedAmount += amount;
                        pc.Discount += amount;
                        pc.OrderAmount += amount; // Because we tip on comped items
                    }
                    else if (amount < 0) // This is a discount, so remember it for tax calculation
                        pc.Discount -= amount;
                    else
                    {
                        pc.Amount += amount;
                        pc.OrderAmount += amount;
                    }
                }
            } // end loop distributing shares
        }
        // At this point we have been through all the line items and added up the totals for each person
        // There is an edge case where some people may have discounts which exceed their costs, if they
        // do we'll reallocate their unused discount to other people evenly. We could be more methodical
        // about this so as to distribute the extra discount according to the shares specified by the user
        // but this is such an unlikely case it hardly seems worthwhile.

        // First, we get a list of the costs that were exceeded by a discount and zero them, adding up the unused discount.
        decimal excessDiscount = 0;
        foreach (PersonCost costItem in Costs.Where(pc => pc.Discount > pc.OrderAmount))
        {
            excessDiscount += costItem.Discount - costItem.OrderAmount;
            costItem.Discount = costItem.OrderAmount;
        }

        // Now it's possible to get a list of just the people who spent money
        PersonCost[] nonZeroCosts = Costs.Where(pc => pc.OrderAmount > pc.Discount).ToArray();

        // if necessary, share out the extra discount 
        if (excessDiscount > 0 && nonZeroCosts.Length > 0)
        {
            int remainingCosts = nonZeroCosts.Length; // How many have not yet been given a share
            // Iterate through the people who have some cost left, sharing out the remaining discounts as evenly as possible.
            // Do the smallest ones first so as to be sure to use up all the excess discount in a single pass 
            foreach (PersonCost costItem in nonZeroCosts.OrderBy(pc => pc.OrderAmount - pc.Discount))
            {
                decimal extraDiscount = costItem.OrderAmount - costItem.Discount; // Each person gets no more discount than they spent
                extraDiscount = Math.Min(extraDiscount, excessDiscount / remainingCosts); // and no more than a share of what's left
                costItem.Discount += extraDiscount;
                excessDiscount -= extraDiscount;
                remainingCosts--;
            }
            Debug.Assert(excessDiscount >= 0, "Excess discount is negative and it shouldn't ever be");
        }

        // Now step through the totals for each person that spent something add in tax and tip, do not tax discounts
        // Tax is shared in proportion to what was actually taxable (so if coupons were taxable, they count)
        // Tip is shared based on what is actually spent, so even though a meal may be discounted, you still get to tip on all of it
        decimal remainingTotal = TotalAmount;
        decimal TipBasis = GetOrderAmount();

        foreach (PersonCost costItem in Costs.Where(pc => pc.OrderAmount > 0)) // So, just the people who bought things
        {
            decimal taxableAmount = Math.Max(0, costItem.ChargedAmount
                - (IsCouponAfterTax ? 0 : (costItem.Discount - costItem.CompedAmount))); // Comped items are included in discount number
            decimal shareOfTax = TaxedAmount > 0 ? Tax * taxableAmount / TaxedAmount : 0;
            decimal shareOfTip = TipBasis > 0 ? Tip * costItem.OrderAmount / TipBasis : 0;
            decimal shareOfSubtotal = costItem.OrderAmount - costItem.Discount;

            costItem.Amount = Math.Round(shareOfSubtotal + shareOfTax + shareOfTip, 2);
            remainingTotal -= costItem.Amount;
        }
        RoundingErrorAmount = Math.Round(remainingTotal, 2);
        /* At this point, there may be a few cents left over, caused by the difference between rounding individual totals 
         * after adding tax and tip and summing the results versus calculating the total, adding tax and tip, then rounding.
         * The difference is generally +/- one cent at most, but it could be as much as +/- one cent per person in theory.  
         * The extra is added (or subtracted from) the first non zero total it would not overwhelm.
        */
        if (IsUnsharedAmountSignificant && nonZeroCosts.Length > 0)
        {
            Utilities.DebugMsg($"In {nameof(DistributeCosts20230527)} : {remainingTotal:C} was unallocated after sharing costs in {DebugDisplay}");
            PersonCost costItem = nonZeroCosts.First(ci => (ci.Amount + remainingTotal) > 0);
            costItem.Amount += remainingTotal;
        }
        // Now that all the costs have been distributed to individuals, recalculate the rounded amount
        RoundedAmount = GetRoundedAmount();
    }

    /// <summary>
    /// Compares the results of the current distribution algorithm with the legacy 2023-05-27 algorithm.
    /// </summary>
    /// <param name="report">If true and the difference exceeds a threshold, a debug message is written.</param>
    /// <returns>The total absolute difference between participant amounts under the two algorithms.</returns>
    internal decimal CompareCostDistribution(bool report = true)
    {
        decimal totalDifference = 0;
        DistributeCosts20230527();
        string s = System.Text.Json.JsonSerializer.Serialize(Costs.ToList());
        System.Collections.Generic.List<PersonCost>? OldCosts = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<PersonCost>>(s);
        DistributeCosts();
        if (OldCosts is null)
            return 0; // Should never happen, but if it does, just say there's no difference
        foreach ((PersonCost? oldPc, PersonCost? newPc) in OldCosts.Zip(Costs))
        {
            if (oldPc is null || newPc is null)
                break;
            totalDifference += Math.Abs(oldPc.Amount - newPc.Amount);
        }
        if (report && totalDifference > 0.25m && UnallocatedAmount == 0)
            Utilities.DebugMsg($"In Meal.CompareCostDistribution: Distribution difference of {totalDifference:C} detected in {DebugDisplay}");
        return totalDifference;
    }
}