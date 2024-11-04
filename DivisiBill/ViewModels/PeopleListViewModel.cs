using DivisiBill.Models;
using CommunityToolkit.Mvvm.Input;
using DivisiBill.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DivisiBill.ViewModels;

partial class PeopleListViewModel: Services.ObservableObjectPlus
{
    #region Globals and constructors
    public delegate void SelectPersonDelegate(Person person, PersonCost personCost);

    private readonly SelectPersonDelegate PersonSelected;
    private readonly Action<Person> ShowPerson;
    private readonly PersonCost personCost;
    public PeopleListViewModel()
    {
    }
    public PeopleListViewModel(SelectPersonDelegate PersonSelectedParameter, Action<Person> ShowPersonParameter, PersonCost personCostParameter = null) : this()
    {
        PersonSelected = PersonSelectedParameter;
        ShowPerson = ShowPersonParameter;
        personCost = personCostParameter;
    }
    #endregion
    #region People Manipulation
    public ObservableCollection<Person> AllPeople => Person.AllPeople;

    [ObservableProperty]
    private bool showPeopleHint = false;
    partial void OnShowPeopleHintChanged(bool value) => App.Settings.ShowPeopleHint = value;

    #region Delete and Undelete
    [RelayCommand]
    private async Task Delete(Person personParam)
    {
        Person p = personParam ?? SelectedPerson;
        if (p is not null)
        {
            if (IsInUse(p))
            {
                await Utilities.ShowAppSnackBarAsync($"Failed: {p.Nickname} cannot be deleted.\nThey are sharing the current bill");
                return;
            }
            if (p == SelectedPerson)
                SelectedPerson = AllPeople.Alternate(SelectedPerson);
            deletedPeople.Push(p);
            IsAnyDeletedPerson = true;
            OnPropertyChanged(nameof(IsManyDeletedPeople));
            AllPeople.Remove(p);
            await Person.SaveSettingsAsync(); 
        }
    }

    private Stack<Person> deletedPeople = new Stack<Person>();

    [RelayCommand]
    public async Task UnDeletePersonAsync()
    {
        if (IsAnyDeletedPerson)
        {
            deletedPeople.Pop().UpsertInAllPeople();
            IsAnyDeletedPerson = deletedPeople.Any();
            await Person.SaveSettingsAsync();
        }
    }

    [RelayCommand]
    public async Task UnDeleteAllPeopleAsync()
    {
        if (IsAnyDeletedPerson)
        {
            while (deletedPeople.Any())
                await UnDeletePersonAsync();
        }
    } 
    public void ForgetDeletedPeople()
    {
        deletedPeople.Clear();
        IsAnyDeletedPerson = false;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManyDeletedPeople))]
    private bool isAnyDeletedPerson = false;
    public bool IsManyDeletedPeople => deletedPeople.Count > 1;
    #endregion

    [RelayCommand]
    public async Task GetRemotePeopleListAsync()
    {
        if (App.IsCloudAllowed)
        {
            var fileListViewModel = new FileListViewModel(RemoteWs.PersonListTypeName);
            await fileListViewModel.InitializeAsync();
            if (fileListViewModel.FileListCount > 0)
            {
                await Shell.Current.Navigation.PushAsync(new Views.FileListPage(fileListViewModel));
                var result = await fileListViewModel.SelectionCompleted.Task;
                if (result is not null)
                {
                    bool loaded = await Person.LoadFromRemoteAsync(result.Name, result.ReplaceRequested);
                    if (!loaded)
                        await Utilities.DisplayAlertAsync("Error", "No remote list found");
                }
            }
            else
                await Utilities.DisplayAlertAsync("Error", "No lists were found");
        }
        else if (!App.Settings.IsCloudAccessAllowed)
            await Utilities.DisplayAlertAsync("Error", "Cloud access is not enabled in program settings");
        else
            await Utilities.DisplayAlertAsync("Error", "Cloud is enabled but currently inaccessible");
    }

    private bool IsInUse(Person p) => Meal.CurrentMeal.Costs.Any(pc => pc.Diner == p);

    [RelayCommand]
    private async Task Use(Person personParam)
    {
        Person p = personParam ?? SelectedPerson;
        if (p is not null)
        {
            if (IsInUse(p))
                await Utilities.ShowAppSnackBarAsync($"Failed: {p.Nickname} is already sharing the current bill");
            else
                PersonSelected?.Invoke(p, personCost); 
        }
    }

    [RelayCommand]
    private async Task Add()
    {
        Person p = new Person(Guid.NewGuid());
        Contact contact = null;
        try
        {
            if (await Utilities.HasContactsReadPermissionAsync()) 
                contact = await Contacts.Default.PickContactAsync();
        }
        catch (PermissionException)
        {
            // Permission denied, just ignore it and offer up a null screen
        }
        catch (Exception ex)
        {
            Utilities.ReportCrash(ex);
        }

        if (contact is not null)
        {
            p.FirstName = contact.GivenName;
            p.MiddleName = contact.MiddleName;
            p.LastName = contact.FamilyName;
            p.Email = contact.Emails.FirstOrDefault()?.EmailAddress;
        }
        ShowDetails(p);
    }

    [RelayCommand]
    private void ShowDetails(Person personParam)
    {
        Person p = personParam ?? SelectedPerson;
        if (p is not null)
            ShowPerson.Invoke(p);
    }

    [ObservableProperty]
    private Person selectedPerson;

#if WINDOWS
    private Person lastPersonSelectedByMe = null;
#endif

    [RelayCommand]
    public void SelectPerson(Person personParam)
    {
#if WINDOWS
        // Unfortunately Windows selects any new item before calling this code
        // probably related to https://github.com/dotnet/maui/issues/5446
        // This kludge works around that as long as you only use this method for selection
        if (personParam == lastPersonSelectedByMe)
        {
            SelectedPerson = null;
            lastPersonSelectedByMe = null;
        }
        else
        {
            SelectedPerson = personParam;
            lastPersonSelectedByMe = personParam;
        }
#else        
        if (personParam == SelectedPerson)
            SelectedPerson = null;
        else if (personParam is not null)
            SelectedPerson = personParam;
#endif
    }

    #endregion
}