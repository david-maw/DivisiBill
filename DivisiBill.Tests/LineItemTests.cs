using DivisiBill.Models;
using System.Collections.ObjectModel;

namespace DivisiBill.Tests
{
    [TestClass]
    public class LineItemTests
    {
        [TestInitialize]
        public void Init()
        {
            // Ensure static state is predictable
            LineItem.NextItemNumber = 1;
        }

        [TestMethod]
        public void Constructor_SetsUpSharedBy()
        {
            var li = new LineItem();
            Assert.IsNotNull(li.SharedBy);
            Assert.HasCount(LineItem.maxSharers, li.SharedBy);
            Assert.IsTrue(li.SharedBy.All(b => b == false));
        }

        [TestMethod]
        public void SetShares_GetShares_TotalShares_TotalSharers_Work()
        {
            var li = new LineItem();
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
            var li = new LineItem();
            // Set some shares explicitly
            li.SetShares(LineItem.DinerID.first, 2); // '2'
            li.SetShares((LineItem.DinerID)2, 1); // '1'
            li.SetShares((LineItem.DinerID)3, 0); // '0'
            string s = li.SharesList;
            // Should contain at least first two characters '2' and '1'
            Assert.StartsWith("21", s);

            // Now test parsing
            var li2 = new LineItem();
            li2.SharesList = "210"; // diner1=2,diner2=1,diner3=0
            Assert.AreEqual(2, li2.GetShares(LineItem.DinerID.first));
            Assert.AreEqual(1, li2.GetShares((LineItem.DinerID)2));
            Assert.AreEqual(0, li2.GetShares((LineItem.DinerID)3));
        }

        [TestMethod]
        public void GetAmounts_DistributesCorrectly()
        {
            var li = new LineItem();
            li.Amount = 30m;
            li.SetShares(LineItem.DinerID.first, 1);
            li.SetShares((LineItem.DinerID)2, 1);
            var amounts = li.GetAmounts();
            // Two sharers, each should get 15
            Assert.AreEqual(15m, amounts[0]);
            Assert.AreEqual(15m, amounts[1]);
        }

        [TestMethod]
        public void GetNextSharer_Works()
        {
            var li = new LineItem();
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
            var li = new LineItem();
            li.SetShares((LineItem.DinerID)4, 1);
            Assert.AreEqual((LineItem.DinerID)4, li.GetMaxDiner());
            // No shares -> none
            var li2 = new LineItem();
            Assert.AreEqual(LineItem.DinerID.none, li2.GetMaxDiner());
        }

        [TestMethod]
        public void EnsureItemName_AssignsDefaultName()
        {
            var li = new LineItem();
            li.ItemName = null;
            LineItem.NextItemNumber = 42;
            li.EnsureItemName();
            Assert.AreEqual("Item 42", li.ItemName);
        }

        [TestMethod]
        public void SwapSharerID_TransfersShares()
        {
            var li = new LineItem();
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
            var li = new LineItem();
            Assert.AreEqual(LineItem.SharingType.None, li.GetSharingType());
            li.SetShares(LineItem.DinerID.first, 1);
            Assert.AreEqual(LineItem.SharingType.Even, li.GetSharingType());
            li.SetShares((LineItem.DinerID)2, 2);
            Assert.AreEqual(LineItem.SharingType.Uneven, li.GetSharingType());
        }

        [TestMethod]
        public void ShareEvenly_SetsOneSharePerCost()
        {
            var li = new LineItem();
            var costs = new ObservableCollection<PersonCost>
            {
                new PersonCost(){ DinerID = LineItem.DinerID.first },
                new PersonCost(){ DinerID = (LineItem.DinerID)2 }
            };
            li.ShareEvenly(costs);
            Assert.AreEqual(1, li.GetShares(LineItem.DinerID.first));
            Assert.AreEqual(1, li.GetShares((LineItem.DinerID)2));
        }

        [TestMethod]
        public void DeallocateShares_ClearsAll()
        {
            var li = new LineItem();
            li.SetShares(LineItem.DinerID.first, 1);
            li.SetShares((LineItem.DinerID)2, 1);
            li.DeallocateShares();
            Assert.AreEqual(0, li.TotalSharers);
            Assert.IsTrue(li.SharedBy.All(b => b == false));
        }

        [TestMethod]
        public void TransferShares_MovesShares()
        {
            var li = new LineItem();
            li.SetShares(LineItem.DinerID.first, 2);
            li.TransferShares((LineItem.DinerID)3, LineItem.DinerID.first);
            Assert.AreEqual(0, li.GetShares(LineItem.DinerID.first));
            Assert.AreEqual(2, li.GetShares((LineItem.DinerID)3));
        }

        [TestMethod]
        public void SharersString_ProducesExpectedSymbols()
        {
            var li = new LineItem();
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
    }
}
