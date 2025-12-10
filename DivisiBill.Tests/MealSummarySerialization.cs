using DivisiBill.Models;
using System.Text;

namespace DivisiBill.Tests;

[TestClass]
public class MealSummarySerialization
{
    public MealSummarySerialization() => DivisiBill.App.Settings = new FakeAppSettings();

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
        byte[] byteArray = Encoding.UTF8.GetBytes(MealSerialization.DefaultMealXml);
        using MemoryStream memoryStream = new(byteArray);
        MealSummary ms = MealSummary.LoadFromMealStream(memoryStream, "dummy");
        Assert.IsNotNull(ms);
        Assert.AreEqual("Queasy Diner", ms.VenueName);
        Assert.AreEqual(2, ms.StoredVersion);
        Assert.AreEqual(DateTime.MinValue, ms.CreationTime);
        Assert.AreEqual("00010101000000", ms.DefaultId);
        Assert.IsTrue(ms.IsDefault);
    }
}
