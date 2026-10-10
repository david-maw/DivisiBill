using DivisiBill.Models;

namespace DivisiBill.Tests;

[TestClass]
public class PersonCostTests
{
    [TestMethod]
    public void DefaultDinerIdDoesNotUnderflowIndex()
    {
        PersonCost personCost = new();

        Assert.AreEqual((byte)0, personCost.DinerIndex);
        Assert.AreEqual(string.Empty, personCost.DinerIDText);
    }

    [TestMethod]
    public void DinerIdMapsToZeroBasedIndexAndSymbol()
    {
        PersonCost personCost = new() { DinerID = LineItem.DinerID.third };

        Assert.AreEqual((byte)2, personCost.DinerIndex);
        Assert.AreEqual("③", personCost.DinerIDText);
    }
}
