using DivisiBill.Models;
using System.Collections.ObjectModel;

namespace DivisiBill.Tests;

[TestClass]
public class MealSummaryGeneral
{
    [TestMethod]
    public void LocationChanged_WithNulls_IsNoOp()
    {
        MealSummary ms = new()
        {
            VenueName = "No Change",
            IsLocal = true,
            IsRemote = false,
        };

        ms.LocationChanged();

        Assert.IsTrue(ms.IsLocal);
        Assert.IsFalse(ms.IsRemote);
        Assert.AreEqual("No Change", ms.VenueName);
    }

    [TestMethod]
    public void TryAddTo_SortsNewestFirstAndRejectsDuplicate()
    {
        MealSummary older = new()
        {
            VenueName = "Older",
            CreationTime = new DateTime(2025, 1, 2, 3, 4, 5),
        };
        MealSummary newer = new()
        {
            VenueName = "Newer",
            CreationTime = new DateTime(2025, 1, 2, 4, 5, 6),
        };
        ObservableCollection<MealSummary> list = [];

        older.TryAddTo(list);
        newer.TryAddTo(list);

        Assert.AreEqual(2, list.Count);
        Assert.AreSame(newer, list[0]);
        Assert.AreSame(older, list[1]);
        Assert.IsFalse(newer.TryAddTo(list));
    }

    [TestMethod]
    public void GetLastChangeString_UsesCreationTimeFallbackAndElapsedSeconds()
    {
        MealSummary unset = new()
        {
            CreationTime = new DateTime(2025, 1, 2, 3, 4, 5),
        };
        Assert.IsNull(unset.GetLastChangeString());

        MealSummary changed = new()
        {
            CreationTime = new DateTime(2025, 1, 2, 3, 4, 5),
            ActualLastChangeTime = new DateTime(2025, 1, 2, 3, 4, 17),
        };

        Assert.AreEqual("13 s later", changed.GetLastChangeString());
    }

    [TestMethod]
    public void FileNameInconsistent_UsesCreationTimeMatch()
    {
        MealSummary ms = new()
        {
            CreationTime = new DateTime(2025, 1, 2, 3, 4, 5),
        };

        Assert.IsTrue(ms.FileNameInconsistent("20250102030400.xml"));
        Assert.IsFalse(ms.FileNameInconsistent("20250102030405.xml"));
    }

    [TestMethod]
    public void ShallowCopy_AndEquality_UseCreationTimeFileName()
    {
        MealSummary original = new()
        {
            VenueName = "Copy Me",
            CreationTime = new DateTime(2025, 2, 3, 4, 5, 6),
        };

        MealSummary copy = original.ShallowCopy();

        Assert.AreNotSame(original, copy);
        Assert.AreEqual(original, copy);
        Assert.AreEqual(original.GetHashCode(), copy.GetHashCode());
        Assert.AreEqual(original.VenueName, copy.VenueName);
        Assert.AreEqual(original.FileName, copy.FileName);
    }
}