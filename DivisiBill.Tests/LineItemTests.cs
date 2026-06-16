using DivisiBill.Models;

namespace DivisiBill.Tests;

[TestClass]
public class LineItemTests
{
    [TestInitialize]
    public void Init() =>
        // Ensure static state is predictable
        LineItem.NextItemNumber = 1;

    [TestMethod]
    public void Constructor_SetsUpSharedBy()
    {
        LineItem li = new();
        Assert.IsNotNull(li.SharedBy);
        Assert.HasCount(LineItem.maxSharers, li.SharedBy);
        Assert.IsTrue(li.SharedBy.All(b => b == false));
    }

    [TestMethod]
    public void SetShares_GetShares_TotalShares_TotalSharers_Work()
    {
        LineItem li = new();
        // Set diner 1 to have 3 shares
        li.SetShares(LineItem.DinerID.first, 3);
        Assert.AreEqual(3, li.GetShares(LineItem.DinerID.first));
        Assert.IsTrue(li.SharedBy[0]);
        Assert.AreEqual(2, li.ExtraShares[0]);
        Assert.AreEqual(3, li.TotalShares);
        Assert.AreEqual(1, li.TotalSharers);

        // Reduce to zero
        li.SetShares(LineItem.DinerID.first, 0);
        Assert.AreEqual(0, li.GetShares(LineItem.DinerID.first));
        Assert.IsFalse(li.SharedBy[0]);
        Assert.AreEqual(0, li.ExtraShares[0]);
        Assert.AreEqual(0, li.TotalShares);
        Assert.AreEqual(0, li.TotalSharers);
    }

    [TestMethod]
    public void SharesList_GetterAndSetter_Works()
    {
        LineItem li = new();
        // Set some shares explicitly
        li.SetShares(LineItem.DinerID.first, 2); // '2'
        li.SetShares((LineItem.DinerID)2, 1); // '1'
        li.SetShares((LineItem.DinerID)3, 0); // '0'
        string s = li.SharesList;
        // Should contain at least first two characters '2' and '1'
        Assert.StartsWith("21", s);

        // Now test parsing
        LineItem li2 = new()
        {
            SharesList = "210" // diner1=2,diner2=1,diner3=0
        };
        Assert.AreEqual(2, li2.GetShares(LineItem.DinerID.first));
        Assert.AreEqual(1, li2.GetShares((LineItem.DinerID)2));
        Assert.AreEqual(0, li2.GetShares((LineItem.DinerID)3));
    }

    [TestMethod]
    public void GetAmounts_DistributesCorrectly()
    {
        LineItem li = new()
        {
            Amount = 30m
        };
        li.SetShares(LineItem.DinerID.first, 1);
        li.SetShares((LineItem.DinerID)2, 1);
        decimal[] amounts = li.GetAmounts();
        // Two sharers, each should get 15
        Assert.AreEqual(15m, amounts[0]);
        Assert.AreEqual(15m, amounts[1]);
    }

    [TestMethod]
    public void GetNextSharer_Works()
    {
        LineItem li = new();
        li.SetShares(LineItem.DinerID.first, 1);
        li.SetShares((LineItem.DinerID)3, 1);
        // Starting from none should return first
        Assert.AreEqual(LineItem.DinerID.first, li.GetNextSharer());
        // Starting from first should return third
        Assert.AreEqual((LineItem.DinerID)3, li.GetNextSharer(LineItem.DinerID.first));
        // Starting from third should return none
        Assert.AreEqual(LineItem.DinerID.none, li.GetNextSharer((LineItem.DinerID)3));
    }

    [TestMethod]
    public void GetMaxDiner_Works()
    {
        LineItem li = new();
        li.SetShares((LineItem.DinerID)4, 1);
        Assert.AreEqual((LineItem.DinerID)4, li.GetMaxDiner());
        // No shares -> none
        LineItem li2 = new();
        Assert.AreEqual(LineItem.DinerID.none, li2.GetMaxDiner());
    }

    [TestMethod]
    public void EnsureItemName_AssignsDefaultName()
    {
        LineItem li = new()
        {
            ItemName = null
        };
        LineItem.NextItemNumber = 42;
        li.EnsureItemName();
        Assert.AreEqual("Item 42", li.ItemName);
    }

    [TestMethod]
    public void SwapSharerID_TransfersShares()
    {
        LineItem li = new();
        li.SetShares(LineItem.DinerID.first, 2);
        li.SetShares((LineItem.DinerID)2, 1);
        // Swap first and second
        li.SwapSharerID((LineItem.DinerID)2, LineItem.DinerID.first);
        Assert.AreEqual(1, li.GetShares(LineItem.DinerID.first));
        Assert.AreEqual(2, li.GetShares((LineItem.DinerID)2));
    }

    [TestMethod]
    public void GetSharingType_ReflectsState()
    {
        LineItem li = new();
        Assert.AreEqual(LineItem.SharingType.None, li.GetSharingType());
        li.SetShares(LineItem.DinerID.first, 1);
        Assert.AreEqual(LineItem.SharingType.Even, li.GetSharingType());
        li.SetShares((LineItem.DinerID)2, 2);
        Assert.AreEqual(LineItem.SharingType.Uneven, li.GetSharingType());
    }

    [TestMethod]
    public void ShareEvenly_SetsOneSharePerCost()
    {
        LineItem li = new();
        List<PersonCost> costs =
        [
            new(){ DinerID = LineItem.DinerID.first },
            new(){ DinerID = LineItem.DinerID.second }
        ];
        li.ShareEvenly(costs);
        Assert.AreEqual(1, li.GetShares(LineItem.DinerID.first));
        Assert.AreEqual(1, li.GetShares(LineItem.DinerID.second));
    }

    [TestMethod]
    public void DeallocateShares_ClearsAll()
    {
        LineItem li = new();
        li.SetShares(LineItem.DinerID.first, 1);
        li.SetShares(LineItem.DinerID.second, 1);
        li.DeallocateShares();
        Assert.AreEqual(0, li.TotalSharers);
        Assert.IsTrue(li.SharedBy.All(b => b == false));
    }

    [TestMethod]
    public void TransferShares_MovesShares()
    {
        LineItem li = new();
        li.SetShares(LineItem.DinerID.first, 2);
        li.TransferShares((LineItem.DinerID)3, LineItem.DinerID.first);
        Assert.AreEqual(0, li.GetShares(LineItem.DinerID.first));
        Assert.AreEqual(2, li.GetShares((LineItem.DinerID)3));
    }

    [TestMethod]
    public void SharersString_ProducesExpectedSymbols()
    {
        LineItem li = new();
        // No sharers -> empty
        Assert.AreEqual(string.Empty, li.Sharers);
        // Single sharer -> circled number
        li.SetShares(LineItem.DinerID.first, 1);
        Assert.AreEqual("①", li.Sharers);
        // Multiple even -> +
        li.SetShares((LineItem.DinerID)2, 1);
        Assert.AreEqual("+", li.Sharers);
        // Uneven -> *
        li.SetShares((LineItem.DinerID)2, 2);
        Assert.AreEqual("*", li.Sharers);
    }

    [TestMethod]
    public void CopyConstructor_CopiesAllFields()
    {
        LineItem original = new()
        {
            ItemName = "Soup",
            Amount = 12.50m,
            SharesList = "21",
            Comped = true,
            FilterForSharerID = LineItem.DinerID.first
        };
        LineItem copy = new(original);
        Assert.AreEqual(original.ItemName, copy.ItemName);
        Assert.AreEqual(original.Amount, copy.Amount);
        Assert.AreEqual(original.SharesList, copy.SharesList);
        Assert.AreEqual(original.Comped, copy.Comped);
        Assert.AreEqual(original.FilterForSharerID, copy.FilterForSharerID);
    }

    [TestMethod]
    public void Amount_RoundsToTwoDecimalPlaces()
    {
        LineItem li = new() { Amount = 1.999m };
        Assert.AreEqual(2.00m, li.Amount);

        li.Amount = 1.234m;
        Assert.AreEqual(1.23m, li.Amount);
    }

    [TestMethod]
    public void IsUnfiltered_TrueByDefault()
    {
        Assert.IsTrue(new LineItem().IsUnfiltered);
    }

    [TestMethod]
    public void IsUnfiltered_FalseWhenFilterSet()
    {
        LineItem li = new() { FilterForSharerID = LineItem.DinerID.first };
        Assert.IsFalse(li.IsUnfiltered);
    }

    [TestMethod]
    public void FilteredAmount_ReturnsFullAmountWhenUnfiltered()
    {
        LineItem li = new() { Amount = 30m };
        li.SetShares(LineItem.DinerID.first, 1);
        li.SetShares(LineItem.DinerID.second, 1);
        Assert.AreEqual(30m, li.FilteredAmount);
    }

    [TestMethod]
    public void FilteredAmount_ReturnsShareAmountWhenFiltered()
    {
        LineItem li = new() { Amount = 30m };
        li.SetShares(LineItem.DinerID.first, 1);
        li.SetShares(LineItem.DinerID.second, 1);
        li.FilterForSharerID = LineItem.DinerID.first;
        Assert.AreEqual(15m, li.FilteredAmount);
    }

    [TestMethod]
    public void IsSharedByFilter_TrueWhenUnfiltered()
    {
        Assert.IsTrue(new LineItem().IsSharedByFilter);
    }

    [TestMethod]
    public void IsSharedByFilter_TrueWhenFilteredSharerHasShares()
    {
        LineItem li = new();
        li.SetShares(LineItem.DinerID.first, 1);
        li.FilterForSharerID = LineItem.DinerID.first;
        Assert.IsTrue(li.IsSharedByFilter);
    }

    [TestMethod]
    public void IsSharedByFilter_FalseWhenFilteredSharerHasNoShares()
    {
        LineItem li = new();
        li.SetShares(LineItem.DinerID.first, 1);
        li.FilterForSharerID = (LineItem.DinerID)2;
        Assert.IsFalse(li.IsSharedByFilter);
    }

    [TestMethod]
    public void Comped_DefaultsFalse()
    {
        Assert.IsFalse(new LineItem().Comped);
    }

    [TestMethod]
    public void Comped_CanBeSet()
    {
        LineItem li = new() { Comped = true };
        Assert.IsTrue(li.Comped);
    }

    [TestMethod]
    public void EnsureItemName_DoesNotChangeExistingName()
    {
        LineItem li = new() { ItemName = "Soup" };
        li.EnsureItemName();
        Assert.AreEqual("Soup", li.ItemName);
    }

    [TestMethod]
    public void EnsureItemName_AssignsDefaultNameForWhitespace()
    {
        LineItem li = new() { ItemName = "   " };
        LineItem.NextItemNumber = 7;
        li.EnsureItemName();
        Assert.AreEqual("Item 7", li.ItemName);
    }

    [TestMethod]
    public void ItemName_AdvancesNextItemNumberWhenHigher()
    {
        LineItem.NextItemNumber = 1;
        _ = new LineItem { ItemName = "Item 5" };
        Assert.AreEqual(6, LineItem.NextItemNumber);
    }

    [TestMethod]
    public void ItemName_DoesNotRollBackNextItemNumber()
    {
        LineItem.NextItemNumber = 10;
        _ = new LineItem { ItemName = "Item 5" };
        Assert.AreEqual(10, LineItem.NextItemNumber);
    }

    [TestMethod]
    public void SharesList_SetToEmptyDeallocatesAll()
    {
        LineItem li = new();
        li.SetShares(LineItem.DinerID.first, 2);
        li.SharesList = "";
        Assert.AreEqual(0, li.TotalSharers);
    }

    [TestMethod]
    public void GetShares_DinerIdNone_ReturnsZero()
    {
        Assert.AreEqual(0, new LineItem().GetShares(LineItem.DinerID.none));
    }

    [TestMethod]
    public void SetShares_DinerIdNone_Throws()
    {
        LineItem li = new();
        Assert.ThrowsExactly<Exception>(() => li.SetShares(LineItem.DinerID.none, 1));
    }

    [TestMethod]
    public void GetAmounts_UnevenShares_DistributesProportionally()
    {
        LineItem li = new() { Amount = 30m };
        li.SetShares(LineItem.DinerID.first, 2);  // 2 of 3 total shares
        li.SetShares((LineItem.DinerID)2, 1);     // 1 of 3 total shares
        decimal[] amounts = li.GetAmounts();
        Assert.AreEqual(20m, amounts[0]);
        Assert.AreEqual(10m, amounts[1]);
    }

    [TestMethod]
    public void ToIndex_ConvertsToZeroBasedIndex()
    {
        Assert.AreEqual(-1, LineItem.DinerID.none.ToIndex());
        Assert.AreEqual(0, LineItem.DinerID.first.ToIndex());
        Assert.AreEqual(1, ((LineItem.DinerID)2).ToIndex());
    }

    [TestMethod]
    public void GetNextSharer_NoSharers_ReturnsNone()
    {
        Assert.AreEqual(LineItem.DinerID.none, new LineItem().GetNextSharer());
    }

    [TestMethod]
    public void FirstSharer_NoSharers_ReturnsNone()
    {
        Assert.AreEqual(LineItem.DinerID.none, new LineItem().FirstSharer);
    }

    [TestMethod]
    public void FirstSharer_ReturnsLowestSharer()
    {
        LineItem li = new();
        li.SetShares((LineItem.DinerID)3, 1);
        Assert.AreEqual((LineItem.DinerID)3, li.FirstSharer);
    }

    [TestMethod]
    public void SharersString_CircledNumberForEachPosition()
    {
        for (int i = 1; i <= LineItem.maxSharers; i++)
        {
            LineItem li = new();
            li.SetShares((LineItem.DinerID)i, 1);
            string expected = ((char)('①' + i - 1)).ToString();
            Assert.AreEqual(expected, li.Sharers, $"Expected circled number for diner {i}");
        }
    }
}
