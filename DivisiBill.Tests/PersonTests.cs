using DivisiBill.Models;

namespace DivisiBill.Tests;

[TestClass]
public class PersonTests
{
    [TestMethod]
    public void NicknameSetterAcceptsNewValueWhenOldNicknameIsNull()
    {
        Person person = new()
        {
            FirstName = "Alice",
            LastName = "Smith"
        };

        person.Nickname = "Al";

        Assert.AreEqual("Al", person.Nickname);
        Assert.AreEqual("Al (Alice Smith)", person.DisplayName);
    }

    [TestMethod]
    public void NicknameSetterAllowsBlankNicknameToBeCleared()
    {
        Person person = new()
        {
            FirstName = "Alice",
            LastName = "Smith",
            NicknameForPersistence = "Al" // used to set nickname field
        };

        person.Nickname = null;

        Assert.AreEqual("Alice", person.Nickname);
    }
}
