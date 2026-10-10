using DivisiBill.Models;

namespace DivisiBill.Tests;

[TestClass]
public class VenueTests
{
    [TestMethod]
    public void VenueNameChangeOnlyRaisesSinglePropertyChange()
    {
        Venue venue = new();
        int count = 0;
        venue.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Venue.Name))
                count++;
        };

        venue.Name = "  Pizza Place  ";

        Assert.AreEqual(1, count);
        Assert.AreEqual("Pizza Place", venue.Name);
    }

    [TestMethod]
    public void VenueNotesChangeOnlyRaisesSinglePropertyChange()
    {
        Venue venue = new();
        int count = 0;
        venue.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Venue.Notes))
                count++;
        };

        venue.Notes = "  Best in town  ";

        Assert.AreEqual(1, count);
        Assert.AreEqual("Best in town", venue.Notes);
    }
}
