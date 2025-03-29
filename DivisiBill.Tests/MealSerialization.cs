using DivisiBill.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;

namespace DivisiBill.Tests;

[TestClass]
public class MealSerialization
{
    internal const string DefaultMealXml = """
        <Meal>
          <CreationReason>ElapsedTime</CreationReason>
          <SaveReason>time</SaveReason>
          <SaverVersion>6.2.24</SaverVersion>
          <DataVersion>1.1</DataVersion>
          <Restaurant>Queasy Diner</Restaurant>
          <CreationTime>0001-01-01T00:00:00-07:00</CreationTime>
          <TipRate>0.2</TipRate>
          <TipOnTax>false</TipOnTax>
          <TaxOnDiscount>false</TaxOnDiscount>
          <TaxRate>0.0775</TaxRate>
          <ScannedSubTotal>125.00</ScannedSubTotal>
          <Costs>
            <PersonCost
              PersonGUID="ff6a1c63-3c0d-4f85-9d13-1f442ee718ee"
              Nickname="Bill"
              DinerIndex="1" />
            <PersonCost
              PersonGUID="45ed1c1c-d48f-4005-9b0d-e1768ad082ec"
              Nickname="Chris"
              DinerIndex="2" />
            <PersonCost
              PersonGUID="2ff7856b-2340-4c4c-98cb-096ccca85f10"
              Nickname="Craig"
              DinerIndex="3" />
          </Costs>
          <LineItems>
            <LineItem
              SharesList="111"
              ItemName="Appetizer"
              Amount="20" />
            <LineItem
              SharesList="111"
              ItemName="Discount"
              Amount="-10" />
            <LineItem
              SharesList="001"
              ItemName="Tasty Chicken"
              Amount="30" />
            <LineItem
              SharesList="1"
              ItemName="Overdone Beef"
              Comped="true"
              Amount="40" />
            <LineItem
              SharesList="21"
              ItemName="Wine"
              Amount="60" />
            <LineItem
              SharesList="01"
              ItemName="Fish &amp; Chips"
              Amount="20" />
            <LineItem
              ItemName="Mystery item"
              Amount="5" />
          </LineItems>
        </Meal>
        """; // End of FakeBillXml

    private Meal DefaultMeal
    {
        get
        {
            if (field == null)
            {
                byte[] byteArray = Encoding.UTF8.GetBytes(MealSerialization.DefaultMealXml);
                using MemoryStream memoryStream = new(byteArray);
                field = Meal.LoadFromStream(memoryStream);
            }
            return field;
        }
    }

    /// <summary>
    /// Create a Meal from XML
    /// </summary>
    [TestMethod]
    public void DeserializeXml()
    {
        Meal meal = DefaultMeal;
        Assert.IsNotNull(meal);
        Assert.AreEqual("Queasy Diner", meal.VenueName);
        Assert.AreEqual("1.1", meal.DataVersion);
        Assert.AreEqual(DateTime.MinValue, meal.CreationTime);
        Assert.AreEqual("00010101000000.xml", meal.FileName);
        Assert.IsTrue(meal.IsDefault);
        // Verify bill totals
        Assert.AreEqual(125, meal.SubTotal);
        Assert.AreEqual(170, meal.RoundedAmount);
        Assert.AreEqual(9.69m, meal.Tax);
        Assert.AreEqual(35, meal.Tip);
        Assert.AreEqual(5, meal.UnallocatedAmount);
        Assert.AreEqual(169.69m, meal.TotalAmount);
        Assert.AreEqual(0.0775, meal.TaxRate);
        Assert.AreEqual(0.20, meal.TipRate);
        // Verify individual item amounts 
        Assert.AreEqual(7, meal.LineItems.Count);
        Assert.AreEqual(20, meal.LineItems[0].Amount);
        Assert.AreEqual(-10, meal.LineItems[1].Amount);
        Assert.AreEqual(30, meal.LineItems[2].Amount);
        Assert.AreEqual(40, meal.LineItems[3].Amount);
        Assert.AreEqual(60, meal.LineItems[4].Amount);
        Assert.AreEqual(20, meal.LineItems[5].Amount);
        Assert.AreEqual(5, meal.LineItems[6].Amount);
        // Verify costs
        Assert.AreEqual(3, meal.Costs.Count);
        Assert.AreEqual("Bill", meal.Costs[0].Nickname);
        Assert.AreEqual("Chris", meal.Costs[1].Nickname);
        Assert.AreEqual("Craig", meal.Costs[2].Nickname);
        Assert.AreEqual(64.03m, meal.Costs[0].Amount);
        Assert.AreEqual(56.03m, meal.Costs[1].Amount);
        Assert.AreEqual(43.25m, meal.Costs[2].Amount);
    }
}
