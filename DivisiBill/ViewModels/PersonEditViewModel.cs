#nullable enable

using CommunityToolkit.Mvvm.Input;
using DivisiBill.Models;
using DivisiBill.Services;

namespace DivisiBill.ViewModels;

internal partial class PersonEditViewModel : ObservableObjectPlus
{
    private readonly Person originalPerson;
    private readonly Action ClosePage;

    public PersonEditViewModel(Person personParameter, Action ClosePageParam)
    {
        originalPerson = personParameter;
        CurrentPerson = new Person(personParameter);
        ClosePage = ClosePageParam;
    }

    public Person CurrentPerson { get; }
    public bool IsInUse => originalPerson.IsInUse;
    public bool HasUnsavedChanges => !CurrentPerson.SameIdentityAs(originalPerson);

    [RelayCommand]
    public async Task DeleteAsync()
    {
        if (!IsInUse)
        {
            Person.AllPeople.Remove(originalPerson);
            await Person.SaveSettingsIfChangedAsync();
            ClosePage.Invoke();
        }
    }

    /// <summary>
    /// Any changes have been made to workingPerson, which was initialized to have the same properties as originalParson. So we can
    /// compare them to see if anything changed.
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    public async Task SaveAsync()
    {
        bool closePage = false;
        try
        {
            bool updatingExistingPerson = Person.AllPeople.Any(p => CurrentPerson.PersonGUID.Equals(p.PersonGUID));
            if (updatingExistingPerson && originalPerson.SameIdentityAs(CurrentPerson)) // Nothing has been changed
                closePage = true; // Just exit without doing anything else
            else if (CurrentPerson.IsEmpty)
            {
                // Require the Person be non-null if it is in use, otherwise remove it from the global list
                if (IsInUse)
                    CurrentPerson.Nickname = originalPerson.DisplayName; // do not close the page since we could not do what the user asked
                else if (updatingExistingPerson && Person.AllPeople.Remove(originalPerson))
                    await Person.SaveSettingsIfChangedAsync();
                else
                    closePage = true; // if we get to here it's a blank new person, so there's nothing to save, just close the page
            }
            else
            {
                if (Person.AllPeople.Any(item => CurrentPerson.IsSame(item) && originalPerson != item))  // There is already another distinct entry that looks like this
                    await Utilities.DisplayAlertAsync("Error", "This person would be the same as an existing person");
                else
                {
                    originalPerson.CopyIdentityFrom(CurrentPerson);
                    originalPerson.UpsertInAllPeople();
                    // If this person is in the current meal (meaning IsInUse would have been true if we had tested it) make sure the nicknames are consistent
                    PersonCost? originalPersonCost = Meal.CurrentMeal.Costs.FirstOrDefault(pc => pc.Diner is not null && pc.Diner == originalPerson);
                    if (originalPersonCost is not null)
                        originalPersonCost.Nickname = originalPerson.Nickname;
                    // Make sure any changes are persisted
                    await Person.SaveSettingsIfChangedAsync();
                    closePage = true;
                }
            }
        }
        catch (Exception)
        {
            closePage = true;
            System.Diagnostics.Debugger.Break();
        }
        if (closePage)
            ClosePage.Invoke();
    }

    [RelayCommand]
    public void Restore() => CurrentPerson.CopyIdentityFrom(originalPerson);
}
