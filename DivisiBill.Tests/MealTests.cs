using DivisiBill.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static DivisiBill.Models.Meal;

namespace DivisiBill.Tests;

[TestClass]
public class MealTests
{
    public MealTests()
    {
        DivisiBill.App.Settings = new FakeAppSettings();
    }
    /// <summary>
    /// Verify correct results of rate calculation (closest multiple of 0.0025)
    /// </summary>
    [TestMethod]
    [DataRow(12.95, 0.81, 0.0625)] // 0.06255
    [DataRow(14.35, 0.93, 0.0650)] // 0.06481
    [DataRow(14.59, 0.93, 0.0625)] // 0.06374
    public void TestSimplestRate(double total, double part, double expected)
    {
        Assert.AreEqual(expected, SimplestRate(Convert.ToDecimal(total), Convert.ToDecimal(part)),
            $"SimplestRate({total}, {part}) returned an unexpected value");
    }

    private Meal GetBasicMeal()
    {
        Meal m = new Meal()
        {
            VenueName = "FakeName",
            CreationTime = DateTime.Now,
            TaxRate = 0.1,
            TipRate = 0.2,
            Costs =
            {
                new PersonCost {Nickname = "Tom", DinerID = (LineItem.DinerID)1 },
                new PersonCost {Nickname = "Dick", DinerID = (LineItem.DinerID)2 },
                new PersonCost {Nickname = "Harry", DinerID = (LineItem.DinerID)3 },
            },
            LineItems =
            {
                // Note the layout of SharesList the leftmost digit is shares for person 1, the next for person 2,
                // and so on from left to right NOT, from right to left, like a normal number
                new LineItem {Amount = 45, SharesList = "111"},                  // 15,15,15
                new LineItem {Amount = 10, SharesList = "101"},                  //  5, 0, 5
                new LineItem {Amount = 10, SharesList = "101", Comped = true},   //  5, 0, 5 
                new LineItem {Amount = -3, SharesList = "201"}, // Coupon        // -2, 0,-1
            }
        };
        return m;
    }

    private void NewTestMeal() => testMeal = null; // so the next request will create a new one

    private Meal testMeal = null;
    private Meal TestMeal => testMeal ??= GetBasicMeal();

    [TestMethod]
    // These are sorted before use, so put them in sorted order for convenience
    [DataRow(false, false, 0, 24.80)]
    [DataRow(false, false, 1, 19.50)]
    [DataRow(false, false, 2, 25.90)]
    [DataRow(false, true, 0, 25.00)]
    [DataRow(false, true, 1, 19.50)]
    [DataRow(false, true, 2, 26.00)]
    [DataRow(true, false, 0, 25.16)]
    [DataRow(true, false, 1, 19.80)]
    [DataRow(true, false, 2, 26.28)]
    [DataRow(true, true, 0, 25.40)]
    [DataRow(true, true, 1, 19.80)]
    [DataRow(true, true, 2, 26.40)]
    public void CostCheck(bool tipOnTax, bool taxOnDiscount, int costIndex, double cost)
    {
        TestMeal.TipOnTax = tipOnTax;
        TestMeal.IsCouponAfterTax = taxOnDiscount;
        TestMeal.FinalizeSetup();
        Assert.AreEqual((decimal)cost, TestMeal.Costs[costIndex].Amount, $"Unexpected value for Costs[{costIndex}]");
    }

    [TestMethod]
    [DataRow(false, false, 52.00, 5.2, 0.00, 13.00)]
    [DataRow(false, true, 55.00, 5.5, 3.00, 13.00)]
    [DataRow(true, false, 52.00, 5.2, 0.00, 14.04)]
    [DataRow(true, true, 55.00, 5.5, 3.00, 14.10)]
    public void VerifyAmounts(bool tipOnTax, bool taxOnDiscount, double subTotal, double tax, double taxableDiscount, double tip)
    {
        TestMeal.TipOnTax = tipOnTax;
        TestMeal.IsCouponAfterTax = taxOnDiscount;
        TestMeal.FinalizeSetup();
        Assert.AreEqual((decimal)subTotal, TestMeal.SubTotal, $"Unexpected value for {nameof(Meal.SubTotal)} when TipOnTax ={tipOnTax}, TaxOnDiscount = {taxOnDiscount}");
        Assert.AreEqual((decimal)taxableDiscount, TestMeal.CouponAmountAfterTax, $"Unexpected value for {nameof(Meal.CouponAmountAfterTax)} when TipOnTax ={tipOnTax}, TaxOnDiscount = {taxOnDiscount}");
        Assert.AreEqual((decimal)tax, TestMeal.Tax, $"Unexpected value for {nameof(Meal.Tax)} when TipOnTax ={tipOnTax}, TaxOnDiscount = {taxOnDiscount}");
        Assert.AreEqual((decimal)tip, TestMeal.Tip, $"Unexpected value for {nameof(Meal.Tip)} when TipOnTax ={tipOnTax}, TaxOnDiscount = {taxOnDiscount}");
        Assert.AreEqual(0, TestMeal.RoundingErrorAmount, $"Unexpected value for {nameof(Meal.RoundingErrorAmount)} when TipOnTax ={tipOnTax}, TaxOnDiscount = {taxOnDiscount}");
    }
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void ValidateTotals(bool tipOnTax, bool taxOnDiscount = false)
    {
        TestMeal.TipOnTax = tipOnTax;
        TestMeal.IsCouponAfterTax = taxOnDiscount;
        TestMeal.FinalizeSetup();
        decimal sum = 0;
        foreach (var pc in TestMeal.Costs)
        {
            sum += pc.Amount;
        }
        Assert.AreEqual(TestMeal.TotalAmount, sum,
            $"Sum of amounts does not match total when TipOnTax ={tipOnTax}, TaxOnDiscount = {taxOnDiscount}");
        Assert.AreEqual(0, TestMeal.RoundingErrorAmount,
            $"Unshared amount nonzero when TipOnTax ={tipOnTax}, TaxOnDiscount = {taxOnDiscount}");
    }
    private Meal GetSharedMeal()
    {
        Meal m = new Meal()
        {
            VenueName = "FakeName",
            CreationTime = new DateTime(2001, 2, 3, 12, 13, 14),
            TaxRate = 0, // Tax and tip are zero to simplify the calculations
            TipRate = 0,
            Costs =
                {
                    new PersonCost {Nickname = "Tom", DinerID = (LineItem.DinerID)1 },
                    new PersonCost {Nickname = "Dick", DinerID = (LineItem.DinerID)2 },
                    new PersonCost {Nickname = "Harry", DinerID = (LineItem.DinerID)3 },
                },
            LineItems =
                {
                    // Note the layout of SharesList the leftmost digit is shares for person 1, the next for person 2,
                    // and so on from left to right NOT, from right to left, like a normal number
                    new LineItem {Amount =  1, SharesList = "111"}, // 0.33 each with 0.01 left over
                    new LineItem {Amount = 10, SharesList = "101"}, // 5 each 
                }
        };
        return m;
    }

    /// <summary>
    /// Validate correct sharing of equal amounts to multiple participants
    /// in a simple meal (no tax, tip, coupons or comped items).
    /// </summary>
    /// <param name="costInx">Which resultant cost to validate</param>
    /// <param name="amount">The value it should have</param>
    [TestMethod]
    [DataRow(0, 5.33)]
    [DataRow(1, 0.34)]
    [DataRow(2, 5.33)]
    public void SimpleSharing(int costInx, double amount)
    {
        Meal SharedMeal = GetSharedMeal();
        SharedMeal.FinalizeSetup();
        Assert.AreEqual((decimal)amount, SharedMeal.Costs[costInx].Amount,
            $"Unexpected amount for {SharedMeal.Costs[costInx].Nickname}");
    }

    private Meal GetEdgeCaseMeal()
    {
        Meal m = new Meal()
        {
            VenueName = "FakeName",
            CreationTime = new DateTime(2001, 2, 3, 12, 13, 14),
            TaxRate = 0.10,
            TipRate = 0.20,
            Costs =
                {
                    new PersonCost {Nickname = "Diner1", DinerID = (LineItem.DinerID)1 },
                    new PersonCost {Nickname = "Diner2", DinerID = (LineItem.DinerID)2 },
                    new PersonCost {Nickname = "Diner3", DinerID = (LineItem.DinerID)3 },
                    new PersonCost {Nickname = "Diner4", DinerID = (LineItem.DinerID)4 },
                },
        };
        return m;
    }

    /// <summary>
    /// Validate correct sharing of excess discount
    /// in a simple meal (no tax, tip, coupons or comped items).
    /// </summary>
    /// <param name="expectedCosts">List of resultant costs to validate</param>
    [TestMethod]
    [DataRow(2, 15, 15, 2)]
    public void SharingEdgeCase1(params double[] expectedCosts)
    {
        Meal SharedMeal = GetEdgeCaseMeal();

        SharedMeal.LineItems = new()
        {
            // Note the layout of SharesList the leftmost digit is shares for person 1, the next for person 2,
            // and so on from left to right NOT, from right to left, like a normal number
            new LineItem {Amount = 40, SharesList = "0110"}, // 20 each
            new LineItem {Amount = 20, SharesList = "1001"}, // 10 each 
            new LineItem {Amount =-40, SharesList = "1111"}, // 20 of coupon share each 
        };

        SharedMeal.FinalizeSetup();
        int i = 0;
        foreach (var pc in SharedMeal.Costs)
        {
            Assert.AreEqual((decimal)expectedCosts[i++], pc.Amount,
                $"Unexpected amount for {pc.Nickname}");
        }
    }

    /// <summary>
    /// Validate correct sharing of excess discount
    /// in a simple meal with more discount than one person needs (no tax, tip, coupons or comped items).
    /// </summary>
    /// <param name="tipOnTax">Whether to tip on the tax or not (commonly true)</param>
    /// <param name="taxOnCoupon">Whether the coupon is after tax (rare)</param>
    /// <param name="expectedCosts">List of resultant costs to validate</param>
    [TestMethod]
    [DataRow(false, false, 8.50, 12.75, 12.75, 0)]
    [DataRow(false, true, 9.50, 14.25, 14.25, 0)]
    [DataRow(true, false, 8.60, 12.90, 12.90, 0)]
    [DataRow(true, true, 9.80, 14.70, 14.70, 0)]
    public void SharingEdgeCase2(bool tipOnTax, bool taxOnCoupon, params double[] expectedCosts)
    {
        Meal SharedMeal = GetEdgeCaseMeal();

        SharedMeal.TipOnTax = tipOnTax;
        SharedMeal.IsCouponAfterTax = taxOnCoupon;

        SharedMeal.LineItems = new()
        {
            // Note the layout of SharesList the leftmost digit is shares for person 1, the next for person 2,
            // and so on from left to right NOT, from right to left, like a normal number
            new LineItem { Amount =  40, SharesList = "0110" }, // 20 each
            new LineItem { Amount =  20, SharesList = "3001" }, // 15/5 split 
            new LineItem { Amount = -40, SharesList = "1111" }, // 10 of coupon share each 
        };

        SharedMeal.FinalizeSetup();
        int i = 0;

        Assert.IsTrue(SharedMeal.RoundingErrorAmount <= 0.01m * SharedMeal.Costs.Count(),
            $"Excessive Rounding Error {SharedMeal.RoundingErrorAmount:C} when TipOnTax = {tipOnTax}, TaxOnDiscount = {taxOnCoupon}");

        Assert.AreEqual(SharedMeal.TotalAmount, SharedMeal.Costs.Sum(pc => pc.Amount),
            $"Total was not equal to the sum of the individual costs when TipOnTax = {tipOnTax}, TaxOnDiscount = {taxOnCoupon}");

        foreach (var pc in SharedMeal.Costs)
        {
            Assert.AreEqual((decimal)expectedCosts[i++], pc.Amount,
                $"Unexpected amount for {pc.Nickname}");
        }
    }

    /// <summary>
    /// Validate correct sharing of excess discount
    /// in a simple meal with more discount than the whole bill needs (no tax, tip, coupons or comped items).
    /// </summary>
    /// <param name="tipOnTax">Whether to tip on the tax or not (commonly true)</param>
    /// <param name="taxOnCoupon">Whether the coupon is after tax (rare)</param>
    /// <param name="expectedCosts">List of resultant costs to validate</param>
    [TestMethod]
    [DataRow(false, false, 3.00, 4.50, 4.50, 0)]
    [DataRow(false, true, 3.00, 4.50, 4.50, 0)]
    [DataRow(true, false, 3.00, 4.50, 4.50, 0)]
    [DataRow(true, true, 3.30, 4.35, 4.35, 0)]
    public void SharingEdgeCase3(bool tipOnTax, bool taxOnCoupon, params double[] expectedCosts)
    {
        Meal SharedMeal = GetEdgeCaseMeal();

        SharedMeal.TipOnTax = tipOnTax;
        SharedMeal.IsCouponAfterTax = taxOnCoupon;

        SharedMeal.LineItems = new()
        {
            // Note the layout of SharesList the leftmost digit is shares for person 1, the next for person 2,
            // and so on from left to right NOT, from right to left, like a normal number
            new LineItem { Amount =  40, SharesList = "0110" }, // 20 each
            new LineItem { Amount =  20, SharesList = "3001" }, // 15/5 split 
            new LineItem { Amount = -80, SharesList = "1111" }, // 20 of coupon share each 
        };

        SharedMeal.FinalizeSetup();
        int i = 0;

        Assert.IsTrue(SharedMeal.RoundingErrorAmount <= 0.01m * SharedMeal.Costs.Count(),
            $"Excessive Rounding Error {SharedMeal.RoundingErrorAmount:C} when TipOnTax = {tipOnTax}, TaxOnDiscount = {taxOnCoupon}");

        Assert.IsTrue(SharedMeal.UnallocatedAmount != 0,
            $"Zero unallocated amount when TipOnTax = {tipOnTax}, TaxOnDiscount = {taxOnCoupon}");

        Decimal measuredTotal = 0;

        foreach (var pc in SharedMeal.Costs)
        {
            Assert.AreEqual((decimal)expectedCosts[i++], pc.Amount,
                $"Unexpected amount for {pc.Nickname}");
            measuredTotal += pc.Amount;
        }

        Assert.AreEqual(taxOnCoupon ? 60 : 0, SharedMeal.SubTotal,
            $"Subtotal did not add up when TipOnTax = {tipOnTax}, TaxOnDiscount = {taxOnCoupon}");

        Assert.AreEqual(measuredTotal, SharedMeal.TotalAmount,
            $"Total did not add up when TipOnTax = {tipOnTax}, TaxOnDiscount = {taxOnCoupon}");
    }
}