#nullable disable
using DivisiBill.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;

namespace DivisiBill.Tests;

[TestClass]
public class MealSummarySerializationTests
{
    public MealSummarySerializationTests() => DivisiBill.App.Settings = new FakeAppSettings();

    internal const string FakeBillXml = """
        <Meal>
          <CreationReason>ElapsedTime</CreationReason>
          <SaveReason>time</SaveReason>
          <SaverVersion>6.2.24</SaverVersion>
          <DataVersion>1.1</DataVersion>
          <Restaurant>Queasy Diner</Restaurant>
          <CreationTime>2025-03-26T10:43:05-07:00</CreationTime>
          <LastChangeTime>2025-03-26T10:43:06-07:00</LastChangeTime>
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

    [TestMethod]
    public void DeserializeTest()
    {
        MealSummary ms = MealSummary.LoadJsonFrom
            ("""{"CreationTime":"2024-12-05T16:53:05-08:00","Restaurant":"Aruba","RoundedAmount":120,"LastChangeTime":"2024-12-05T16:53:06-08:00","StoredVersion":2}""");
        // Note that RoundedAmount is ignored, it is no longer used
        Assert.IsNotNull(ms);
        Assert.AreEqual("Aruba", ms.VenueName);
        Assert.AreEqual(2, ms.StoredVersion);
        Assert.AreEqual(new DateTime(2024, 12, 5, 16, 53, 5), ms.CreationTime);
        Assert.AreEqual(new DateTime(2024, 12, 5, 16, 53, 6), ms.ActualLastChangeTime);
        Assert.AreEqual("20241205165305", ms.DefaultId);

        // Test a null Restaurant (MealSummary.VenueName) which occasionally turns up in ancient bills
        ms = MealSummary.LoadJsonFrom
            ("""{"CreationTime":"2024-12-05T06:07:08-08:00","Restaurant":null,"StoredVersion":2}""");
        Assert.IsNotNull(ms);
        Assert.IsNull(ms.VenueName);
        Assert.AreEqual(2, ms.StoredVersion);
        Assert.AreEqual(new DateTime(2024, 12, 5, 6, 7, 8), ms.CreationTime);
        Assert.AreEqual(DateTime.MinValue, ms.ActualLastChangeTime);
        Assert.AreEqual("20241205060708", ms.DefaultId);
#if DEBUG && FALSE
        // Display the property names and an indication of which ones are settable and persisted
        Debug.WriteLine("Properties of the object:");
        foreach (var property in typeof(MealSummary).GetProperties().OrderBy(pr => pr.Name))
        {
            Debug.WriteLine((property.CanWrite ? "*" : " ")
                + (property.CustomAttributes.Any(att => att.AttributeType.Name.Equals("XmlIgnoreAttribute")) ? " " : "*")
                + property.Name + " = " + property.GetValue(ms));
        }
#endif
    }

    [TestMethod]
    public void RoundTripTest()
    {
        MealSummary ms1 = new()
        {
            VenueName = "Test Venue",
            CreationTime = new DateTime(2025, 1, 2, 3, 4, 5),
            ActualLastChangeTime = new DateTime(2025, 1, 2, 13, 14, 15),
        };
        string jsonData = ms1.GetJsonString();
        MealSummary ms = MealSummary.LoadJsonFrom(jsonData);
        Assert.AreEqual(ms1.VenueName, ms.VenueName);
        Assert.AreEqual(ms1.CreationTime, ms.CreationTime);
        Assert.AreEqual(ms1.ActualLastChangeTime, ms.ActualLastChangeTime);
    }

    /// <summary>
    /// Create a mealSummary from a full meal XML
    /// </summary>
    [TestMethod]
    public void DeserializeXmlTest()
    {
        byte[] byteArray = Encoding.UTF8.GetBytes(FakeBillXml);
        using MemoryStream memoryStream = new(byteArray);
        MealSummary ms = MealSummary.LoadFromMealStream(memoryStream, "dummy");
        Assert.IsNotNull(ms);
        Assert.AreEqual("Queasy Diner", ms.VenueName);
        Assert.AreEqual(2, ms.StoredVersion);
        Assert.AreEqual(new DateTime(2025, 3, 26, 10, 43, 5), ms.CreationTime);
        Assert.AreEqual(new DateTime(2025, 3, 26, 10, 43, 6), ms.ActualLastChangeTime);
        Assert.AreEqual("20250326104305", ms.DefaultId);
    }
}
