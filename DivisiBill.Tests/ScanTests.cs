using DivisiBill.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DivisiBill.Tests
{
    [TestClass]
    public class ScanTests
    {
        [TestMethod]
        [DataRow("Simple ", "1.23", 1.23)]
        [DataRow("No leading zero ", ".23", 0.23)]
        [DataRow("Extra trailing zero ", "0.230", 0.23)]
        [DataRow("No decimal separator ", "9", 9)]
        [DataRow("Empty ", "", 0)]
        [DataRow("No digits ", "X", 0, "No digits X")]
        [DataRow("Short ", "X9", 9, "Short X")]
        [DataRow("Leading dollar sign ", "$2.35", 2.35)]
        [DataRow("Leading dollar sign with space ", "$ 2.34", 2.34)]
        [DataRow("Leading text ", "is here $3.33", 3.33, "Leading text is here")]
        [DataRow("Embedded in text ", " both before $4.33 and after", 4.33, "Embedded in text both before")]
        [DataRow("Leading text and spaces ", "before   $   4.33 and after", 4.33, "Leading text and spaces before")]
        [DataRow("Insufficient digits ", "9.5", 9.5)]
        [DataRow("Too many digits ", "9.599", 9.59)]
        [DataRow("Too many decimal separators ", "123.456.78", 456.78, "Too many decimal separators 123.")]
        [DataRow("Multiple numbers ", "1.23 3.78", 3.78, "Multiple numbers 1.23")]
        public void BasicLineItem(string name, string costString, double costParam, string expectedName = null)
        {
            decimal cost = (decimal)costParam;
            OrderLine orderLine = new OrderLine() { ItemName = name, ItemCost = costString };
            LineItem lineItem = orderLine.ToLineItem();

            Assert.AreEqual(expectedName == null ? name : expectedName, lineItem.ItemName, "Item name was not transferred correctly");
            Assert.AreEqual(cost, lineItem.Amount, "Item cost was not scanned correctly");
        }
    }
}
