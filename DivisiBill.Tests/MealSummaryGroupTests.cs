using DivisiBill.Models;

namespace DivisiBill.Tests;

[TestClass]
public class MealSummaryGroupTests
{
    [TestMethod]
    public void Constructor_AddsMealAndInitializesCount()
    {
        DateTime creationTime = new(2025, 1, 2, 3, 4, 5);
        MealSummary summary = new()
        {
            VenueName = "Test Venue",
            CreationTime = creationTime,
        };

        MealSummaryGroup group = new(summary);

        Assert.AreEqual("Test Venue", group.VenueName);
        Assert.AreEqual(creationTime, group.CreationTime);
        Assert.AreEqual(1, group.Count);
        Assert.AreEqual(1, group.MealSummaries.Count);
        Assert.AreSame(summary, group.MealSummaries[0]);
        Assert.AreEqual("1", group.CountText);
    }

    [TestMethod]
    public void MealSummaries_CollectionChanges_UpdateSelectedCountAndText()
    {
        MealSummary baseSummary = new()
        {
            VenueName = "Test Venue",
            CreationTime = new DateTime(2025, 1, 2, 3, 4, 5),
        };
        MealSummaryGroup group = new(baseSummary);

        MealSummary selected = new()
        {
            VenueName = "Test Venue",
            CreationTime = new DateTime(2025, 1, 2, 4, 4, 5),
            FileSelected = true,
        };

        group.MealSummaries.Add(selected);

        Assert.AreEqual(2, group.Count);
        Assert.AreEqual(1, group.SelectedCount);
        Assert.AreEqual("1 of 2", group.CountText);

        group.MealSummaries.Remove(selected);

        Assert.AreEqual(1, group.Count);
        Assert.AreEqual(0, group.SelectedCount);
        Assert.AreEqual("1", group.CountText);
    }

    [TestMethod]
    public void FirstMealSummaries_OnlyReturnsFirstNineItems()
    {
        MealSummary baseSummary = new()
        {
            VenueName = "Test Venue",
            CreationTime = new DateTime(2025, 1, 2, 3, 4, 5),
        };
        MealSummaryGroup group = new(baseSummary);

        for (int i = 0; i < 12; i++)
        {
            group.MealSummaries.Add(new MealSummary
            {
                VenueName = "Test Venue",
                CreationTime = new DateTime(2025, 1, 2, 3, 5 + i, 0),
            });
        }

        Assert.AreEqual(13, group.Count);
        Assert.AreEqual(9, group.FirstMealSummaries.Count);
        Assert.IsTrue(group.FirstMealSummaries.All(ms => ms.CreationTime >= new DateTime(2025, 1, 2, 3, 5, 0) || ms == baseSummary));
    }

    [TestMethod]
    public void CompareCreationTimeTo_SortsNewestFirst()
    {
        MealSummaryGroup older = new(new MealSummary
        {
            VenueName = "Test Venue",
            CreationTime = new DateTime(2025, 1, 2, 3, 4, 5),
        });
        MealSummaryGroup newer = new(new MealSummary
        {
            VenueName = "Test Venue",
            CreationTime = new DateTime(2025, 1, 2, 4, 4, 5),
        });

        Assert.IsTrue(older.CompareCreationTimeTo(newer) > 0);
        Assert.IsTrue(newer.CompareCreationTimeTo(older) < 0);
        Assert.AreEqual(-1, MealSummaryGroup.CompareCreationTimeTo(newer, older));
    }

    [TestMethod]
    public void NotifyDistanceChanged_RaisesPropertyChanged()
    {
        MealSummaryGroup group = new(new MealSummary
        {
            VenueName = "Test Venue",
            CreationTime = new DateTime(2025, 1, 2, 3, 4, 5),
        });

        bool raised = false;
        group.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MealSummaryGroup.Distance))
                raised = true;
        };

        group.NotifyDistanceChanged();

        Assert.IsTrue(raised);
    }
}
