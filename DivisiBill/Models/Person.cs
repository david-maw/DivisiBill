using DivisiBill.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace DivisiBill.Models;

// Primarily needed because List<Tuple<Guid, Guid>> in Xamarin Forms breaks XmlSerializer
public class GuidMappingEntry // needed because the XmlSerializer fails with List<Tuple<Guid,Guid>>
{
    public Guid Key { get; set; }
    public Guid Value { get; set; }
}

[DataContract(Name = "Person", Namespace = "http://schemas.datacontract.org/2004/07/DivisiBill.DataStore")]
[DebuggerDisplay("{DisplayName} - {PersonGUID.ToString()}")]
public class Person : INotifyPropertyChanged, IComparable<Person>
{
    public const string PersonFolderName = "People";
    public const string PersonFileName = "People.xml";
    public const string FromBill = "From Bill";
    private static string PersonPathName = null;
    public static ObservableCollection<Person> AllPeople = [];

    static Person() => AllPeople.CollectionChanged += AllPeople_CollectionChanged;

    private static void CreateFakePeople()
    {
        // They've got fixed GUID values (the first was randomly generated) so as to simplify mix and match between bills and people lists of varying provenances
        // This list is intentionally not in alphabetical order so as to validate sorting in AllPeople
        var defaultPeople = new List<Person>() {
            new("8C720F0B-7062-4482-B55E-E7E19DCF3791") {FirstName = "John",       LastName = "Smith"},
            new("8C720F0B-7062-4482-B55E-E7E19DCF3792") {FirstName = "Robert",     LastName = "Smith",                  Nickname="Bob"},
            new("8C720F0B-7062-4482-B55E-E7E19DCF3793") {FirstName = "Chris",      LastName = "Sells"},
            new("8C720F0B-7062-4482-B55E-E7E19DCF3794") {FirstName = "Craig",      LastName = "Brown"},
            new("8C720F0B-7062-4482-B55E-E7E19DCF3795") {                                                               Nickname = "Support",     Email = "support@autopl.us"},
            new("8C720F0B-7062-4482-B55E-E7E19DCF3796") {FirstName = "Evangeline", LastName = "Throatwarbler-Mangrove", Nickname="Evie"}
        };
        defaultPeople.Sort();
        AllPeople.Clear();
        foreach (var person in defaultPeople)
            AllPeople.Add(person);
        UpdateTime = DateTime.MinValue; // Note that the list contains only default values
    }
    public static async Task InitializeAsync(string BasePathName)
    {
        PersonPathName = Path.Combine(BasePathName, PersonFolderName, PersonFileName);
        await InitializeAsync();
    }

    private static void AllPeople_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
        {
            foreach (Person person in e.NewItems)
                Aliases.Add(person.PersonGUID, person);
        }
        else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
        {
            foreach (Person person in e.OldItems)
                Aliases.Remove(person.PersonGUID);
        }
        else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
        {
            Aliases.Clear();
            foreach (Person person in e.NewItems)
                Aliases.Add(person.PersonGUID, person);
        }
        else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        {
            Aliases.Clear();
        }
        else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move)
        {
            // There should already be an alias entry, so there's nothing to do
        }
        else
            throw new NotImplementedException();
        UpdateTime = DateTime.Now; // Note that the list has been updated
    }

    private static bool LoadFromStream(Stream allPeopleStream, bool doReplace)
    {
        if (allPeopleStream is null)
            return false;
        else
        {
            Updater = App.Settings.PeopleUpdater;
            if (Updater == Guid.Empty)
                Updater = App.Current.Id; // Set the current appid
            Utilities.DebugExamineStream(allPeopleStream);
            DeserializeAllPeople(allPeopleStream, doReplace);
            return true;
        }
    }

    public static async Task<bool> LoadFromRemoteAsync(string name, bool replace)
    {
        Stream stream = null;
        if (App.IsCloudAllowed)
            stream = await RemoteWs.GetItemStreamAsync(RemoteWs.PersonListTypeName, name);
        if (stream is null)
            return false;
        else
            try
            {
                if (LoadFromStream(stream, replace))
                {
                    await SaveSettingsAsync(remote: false);
                    return true;
                }
            }
            catch (Exception ex)
            {
                ex.ReportCrash();
            }
        return false;
    }

    private static bool LoadFromLocal()
    {
        Stream allPeopleStream = new FileStream(PersonPathName, FileMode.Open, FileAccess.Read);
        if (allPeopleStream is not null)
            try
            {
                DateTime savedUpdateTime = App.Settings.PeopleUpdateTime;
                if (savedUpdateTime == DateTime.MinValue)
                    savedUpdateTime = File.GetCreationTime(PersonPathName);
                LoadFromStream(allPeopleStream, true);
                //The deserialize operation changes the update time when it adds items to the list, so restore the old time
                UpdateTime = savedUpdateTime;
                return true;
            }
            catch (Exception ex)
            {
                ex.ReportCrash();
            }
        return false;
    }

    private static async Task InitializeAsync()
    {
        Utilities.DebugMsg("Enter Person.InitializeAsync");
        bool loaded = false;
        if (File.Exists(PersonPathName))
            loaded = LoadFromLocal();
        if (loaded)
            await Utilities.StatusMsgAsync("Person list loaded locally");
        else if (App.IsCloudAllowed)
        {
            loaded = await LoadFromRemoteAsync(null, true); // Pass Null for name to select most recent one
            if (loaded)
                await Utilities.StatusMsgAsync("Person list loaded from cloud");
        }
        if (!loaded)
        {
            CreateFakePeople();
            await Utilities.StatusMsgAsync("Person list set to default values");
        }
        Utilities.DebugMsg($"Exit Person.InitializeAsync, AllPeople.Count = {AllPeople.Count}");
    }

    public static async Task SaveSettingsIfChangedAsync()
    {
        if (!IsSaved)
            await SaveSettingsAsync();
    }

    /// <summary>
    /// Save the list of people, first serialize the list to stream, then
    /// copy that stream to the cloud and locally, whatever is permitted.
    /// </summary>
    /// <returns></returns>
    public static async Task SaveSettingsAsync(bool remote = true)
    {
        bool failed = true;
        using MemoryStream stream = new(10000);
        SerializeAllPeople(stream);
        stream.Position = 0;
        Utilities.DebugExamineStream(stream);
        // Initiate local backup if it is permitted
        Directory.CreateDirectory(Path.GetDirectoryName(PersonPathName));
        try
        {
            File.Delete(PersonPathName);
            using (Stream file = new FileStream(PersonPathName, FileMode.CreateNew, FileAccess.Write))
            {
                await stream.CopyToAsync(file);
            }
            App.Settings.PeopleUpdateTime = UpdateTime;
            App.Settings.PeopleUpdater = Updater;
            failed = false;
        }
        catch (IOException ex)
        {
            Utilities.DebugMsg($"In Person.{nameof(SaveSettingsAsync)}, exception {ex}");
            // Put it in the output stream, but just go on
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
        }
        // Initiate backup to cloud if it is permitted
        if (remote && App.IsCloudAllowed)
        {
            stream.Position = 0;
            await RemoteWs.PutItemStreamAsync(RemoteWs.PersonListTypeName, stream);
        }
        if (failed)
            File.Delete(PersonPathName);
    }

    private static readonly Dictionary<Guid, Person> Aliases = [];

    public event PropertyChangedEventHandler PropertyChanged;

    internal static DateTime UpdateTime { get; set; }

    internal static bool IsDefaultList => UpdateTime == DateTime.MinValue;
    internal static bool IsSaved => IsDefaultList || App.Settings.PeopleUpdateTime == UpdateTime;

    public static Guid Updater { get; set; }
    public static Person FindByNickname(string targetNickname) => AllPeople.FirstOrDefault(person => person.Nickname.Equals(targetNickname, StringComparison.OrdinalIgnoreCase));

    public static Person FindByGuid(Guid PersonGUID)
    {
        Aliases.TryGetValue(PersonGUID, out Person p);
        return p;
    }
    public int CompareTo(Person otherPerson) => string.Compare(this.DisplayName, otherPerson.DisplayName, ignoreCase: true);

    private static readonly DataContractSerializer peopleSerializer = new(typeof(List<Person>));
    private static readonly DataContractSerializer aliasSerializer = new(typeof(Dictionary<Guid, Guid>));


    public static List<Person> DeserializeList(Stream s)
    {
        var reader = XmlReader.Create(s);
        while (reader.Read())
        {
            if (peopleSerializer.IsStartObject(reader))
            {
                var newPeople = (List<Person>)peopleSerializer.ReadObject(reader);
                newPeople.Sort(); // The list should already be sorted, but in case it is not, or the sort order changes, sort it here
                return newPeople;
            }
        }
        return null;
    }

    /// <summary>
    /// Deserialize from the provided stream into AllPeople and Aliases
    /// Called during initialization and whenever the user reloads the list from remote
    /// </summary>
    /// <param name="s"></param>
    /// <param name="doReplace"></param>
    public static void DeserializeAllPeople(Stream s, bool doReplace)
    {
        var reader = XmlReader.Create(s);
        while (reader.Read())
        {
            if (peopleSerializer.IsStartObject(reader))
            {
                if (doReplace)
                    AllPeople.Clear(); // Discard the existing ones (will also clear Aliases)
                var newPeople = (List<Person>)peopleSerializer.ReadObject(reader);
                newPeople.Sort(); // The list should already be sorted, but in case it is not, or the sort order changes, sort it here
                foreach (var newPerson in newPeople)
                {
                    if (newPerson.personGUID.Equals(Guid.Empty)) // Created by an earlier release with a bug
                        newPerson.personGUID = Guid.NewGuid();
                    if (Aliases.ContainsKey(newPerson.personGUID))
                    {
                        // We've seen this person before, so just merge in a few attributes and leave it at that
                        Person existingPerson = Aliases[newPerson.personGUID];
                        existingPerson.Merge(newPerson);
                    }
                    else // we have not seen this person (identified by their GUID) yet
                    {
                        Person p = AllPeople.FirstOrDefault(item => newPerson.IsSame(item));
                        if (p is null)
                            newPerson.UpsertInAllPeople(); // This is a brand new person
                        else
                            Aliases.Add(newPerson.PersonGUID, p); // Someone we know, but with a different PersonGUID
                    }
                }
            }
            if (aliasSerializer.IsStartObject(reader))
            {
                var storedAliases = (Dictionary<Guid, Guid>)aliasSerializer.ReadObject(reader);
                foreach (var alias in storedAliases)
                {
                    Person p = FindByGuid(alias.Value);
                    // Insert the person, but a bad alias list could contain them twice! 
                    if (p is not null && !Aliases.ContainsKey(alias.Key))
                        Aliases.Add(alias.Key, p);
                }
            }
        }
        if (doReplace)
            MergeParticipants(); // the people on the current bill
        else
            MergeSimilarPeople(); // there may be duplicate names with differing GUIDs
    }

    /// <summary>
    /// Adds the current person into the AllPeople list if it isn't already there
    /// returns it or the corresponding Person that was already in the list
    /// </summary>
    public Person PersonFromList()
    {
        if (Aliases.ContainsKey(personGUID))
            return FindByGuid(personGUID); // Another object with the same GUID
        else // we have not seen this person (identified by their GUID) yet
        {
            Person p = AllPeople.FirstOrDefault(item => IsSame(item));
            if (p is null)
            {
                if (personGUID.Equals(Guid.Empty))
                    personGUID = Guid.NewGuid();
                UpsertInAllPeople(); // This is a brand new person, insert it in the correct place
                UpdateTime = DateTime.Now;
                return this;
            }
            else
            {
                if (!personGUID.Equals(Guid.Empty))
                    Aliases.Add(PersonGUID, p); // Someone we know, but with a different PersonGUID
                UpdateTime = DateTime.Now;
                return p;
            }
        }
    }

    /// <summary>
    /// Takes a list of people and either replaces the current list or inserts any that are not already there
    /// </summary>
    /// <param name="newPeople">The list of added people</param>
    /// <param name="replace">Whether to delete the existing people first</param>
    public static void AddPeople(IEnumerable<Person> newPeople, bool replace)
    {
        if (replace)
        {
            AllPeople.Clear();
            Aliases.Clear();
        }
        foreach (var newPerson in newPeople)
            newPerson.PersonFromList();
    }

    public static List<GuidMappingEntry> AliasGuidList
    {
        get
        {
            var storedAliases = new Dictionary<Guid, Person>(Aliases);
            foreach (Person person in AllPeople)
                storedAliases.Remove(person.PersonGUID);
            field.Clear();
            foreach (var alias in storedAliases)
                field.Add(new GuidMappingEntry() { Key = alias.Key, Value = alias.Value.PersonGUID });
            return field;
        }

        set
        {
            field.Clear();
            foreach (var entry in value)
            {
                if (FindByGuid(entry.Key) == null // there is no existing person
                    && !Aliases.ContainsKey(entry.Key))
                {
                    // What we would expect, there is no existing alias entry, or person with this guid
                    Person person = FindByGuid(entry.Value);
                    if (person is not null)
                        Aliases.Add(entry.Key, person);
                    else
                        Services.Utilities.DebugMsg($"In Person.AliasGuidList.Set: There is no person {entry.Value}");
                }
                Services.Utilities.DebugMsg($"In Person.AliasGuidList.Set: {entry.Key} is already used");
            }
            field = value;
        }
    } = [];

    public static void SerializeAllPeople(Stream s)
    {
        XmlWriterSettings settings = new()
        {
            Indent = true
        };
        var writer = XmlWriter.Create(s, settings);
        writer.WriteStartDocument(true);
        writer.WriteStartElement("DivisiBill-People");
        var people = new List<Person>(AllPeople);
        people.Sort();
        peopleSerializer.WriteObject(writer, people);
        var storedAliases = new Dictionary<Guid, Guid>();
        foreach (var alias in Aliases)
            storedAliases.Add(alias.Key, alias.Value.PersonGUID);
        foreach (Person person in AllPeople)
            storedAliases.Remove(person.PersonGUID);
        if (storedAliases.Count > 0)
        {
            aliasSerializer.WriteObject(writer, storedAliases);
        }
        writer.WriteEndElement();
        writer.WriteEndDocument();
        writer.Flush();
        s.Flush();
    }
    #region Person Data 
    #region Data Elements
    private string firstName;
    private string middleName;
    private string lastName;
    private string email;
    private Guid personGUID;
    #endregion
    #region Constructors
    public Person() // public for XAML / XmlSerializer deserialize
    {
    }

    public Person(Guid guid) => PersonGUID = guid == Guid.Empty ? Guid.NewGuid() : guid;
    public Person(string guidString) : this(Guid.ParseExact(guidString, "D")) { } // Guid in format 00000000-0000-0000-0000-000000000000
    public Person(Person p) : this(Guid.NewGuid())
    {
        nickname = p.nickname;
        firstName = p.firstName;
        middleName = p.middleName;
        lastName = p.lastName;
        email = p.email;
    }
    #endregion
    #region Utilities
    public void CopyIdentityFrom(Person p)
    {
        Nickname = p.nickname;
        FirstName = p.firstName;
        MiddleName = p.middleName;
        LastName = p.lastName;
        Email = p.email;
    }

    /// <summary>
    /// Does this person have the same properties as the current one, this is a stronger test than IsSame
    /// </summary>
    /// <param name="p">The person object to check against</param>
    /// <returns>true of the Person has identical property values</returns>
    public bool SameIdentityAs(Person p) => SameString(nickname, p.nickname)
           && SameString(firstName, p.firstName)
           && SameString(middleName, p.middleName)
           && SameString(lastName, p.lastName)
           && SameString(email, p.email);
    private static bool SameString(string s1, string s2) => (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2))
            || string.Equals(s1, s2, StringComparison.CurrentCulture);

    /// <summary>
    /// Does this person 'look like' the current one, this is a weaker test than SameIdentityAs
    /// </summary>
    /// <param name="p">The person object to check against</param>
    /// <returns>true of the Person has identical property values</returns>
    public bool IsSame(Person p)
    {
        bool same =
            this == p ||
            (SameString(Nickname, p.Nickname)
            && SameString(FullName, p.FullName)
            && SameString(Email, p.Email));
        return same;
    }

    public bool IsEmpty => (
               string.IsNullOrWhiteSpace(nickname)
               && Complexity() == 0
               );

    private int Complexity()
    {
        int complexityScore = 0;
        if (!string.IsNullOrWhiteSpace(firstName)) complexityScore += 1;
        if (!string.IsNullOrWhiteSpace(middleName)) complexityScore += 1;
        if (!string.IsNullOrWhiteSpace(lastName)) complexityScore += 5;
        if (!string.IsNullOrWhiteSpace(email)) complexityScore += 10;
        return complexityScore;
    }

    /// <summary>
    /// Determine if the base string "includes" the includedString. A null or blank string is included by anything, otherwise they must be equal 
    /// </summary>
    /// <param name="baseString"></param>
    /// <param name="includedString"></param>
    /// <returns></returns>
    private static bool StringIncludes(string baseString, string includedString) => string.IsNullOrEmpty(includedString)
                  || (!string.IsNullOrEmpty(baseString)
                    & baseString.Equals(includedString, StringComparison.CurrentCulture));

    private bool Includes(Person p) => SameString(nickname, p.nickname)
                && StringIncludes(firstName, p.firstName)
                && StringIncludes(middleName, p.middleName)
                && StringIncludes(lastName, p.lastName)
                && StringIncludes(email, p.email);

    public bool UpsertInAllPeople()
    {
        int index = -1, newIndex = -1;
        foreach (var item in AllPeople)
        {
            index++;
            if (item == this)
            {
                // WTF, it's already present
                return false;
            }
            else if (newIndex < 0 && CompareTo(item) <= 0)
            {
                newIndex = index;
                break;
            }
        }
        if (newIndex < 0)
            AllPeople.Add(this);// it should be at the end
        else
            AllPeople.Insert(newIndex, this);
        return true;
    }
    public void Merge(Person p)
    {
        nickname ??= p.nickname;
        firstName ??= p.firstName;
        middleName ??= p.middleName;
        lastName ??= p.lastName;
        email ??= p.email;
    }

    /// <summary>
    /// Add the participants in the current bill, if they are not already there.
    /// </summary>
    public static void MergeParticipants()
    {
        if (Meal.CurrentMeal?.Costs is null)
            return;
        foreach (var pc in Meal.CurrentMeal.Costs)
        {
            pc.Diner ??= new Person(pc.PersonGUID) { nickname = pc.Nickname, lastName = FromBill };
            Person personInList = pc.Diner.PersonFromList();
            pc.Diner = personInList;
        }
    }

    public static void MergeSimilarPeople()
    {
        var people = AllPeople.OrderBy(p => p.Nickname).ThenBy(p => p.Complexity()).ToArray();
        Person currentPerson = people[^1];
        for (int i = people.Length - 2; i >= 0; i--)
        {
            Person nextPerson = people[i];
            if (currentPerson.Includes(nextPerson))
            {
                currentPerson.Merge(nextPerson);
                Aliases[nextPerson.PersonGUID] = currentPerson;
                AllPeople.Remove(nextPerson);
            }
            else
            {
                currentPerson = nextPerson;
            }
        }
    }

    private static bool StringHasChanged(ref string oldString, string newString)
    {
        if (string.IsNullOrWhiteSpace(oldString) && string.IsNullOrWhiteSpace(newString))
            return false;
        else if (string.Compare(oldString, newString) != 0)
        {
            oldString = newString;
            return true;
        }
        else
            return false;
    }
    // Move a person to its correct place in the sorted list of all people
    private void MoveToCorrectPlace() => AllPeople.Upsert(this);

    protected virtual void OnPropertyChanged([CallerMemberName] string propChanged = null)
    {
        // Verify that it is in the list before moving it around 
        if (AllPeople is not null && AllPeople.Contains(this))
        {
            if (propChanged.Equals(nameof(DisplayName)))
                MoveToCorrectPlace(); // The place in the list will have changed
            UpdateTime = DateTime.Now; // Note that the list has been updated
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propChanged));
    }
    #endregion
    #region Properties
    [DataMember(EmitDefaultValue = false)]
    public Guid PersonGUID
    {
        // Basically, this can only be set once, and reading it sets it
        get
        // Basically, this can only be set once, and reading it sets it
        => personGUID;
        set
        {
            if ((personGUID == Guid.Empty) && (value != Guid.Empty))
            {
                personGUID = value;
                // Note that OnPropertyChanged is not called, this value is not observeable
            }
        }
    }
    public bool IsInUse => Meal.CurrentMeal.Costs.Any(pc => pc.Diner == this);
    public string DisplayName
    {
        get
        {
            var s = new StringBuilder();
            if ((Nickname != FirstName) || (middleName is not null))
                s.Append(firstName);
            if (middleName is not null)
                s.Append(" " + middleName);
            if (lastName is not null)
                s.Append(" " + lastName);
            string y = s.ToString().Trim();
            return !string.IsNullOrEmpty(y) && (y != Nickname) ? Nickname + " (" + y + ")" : Nickname;
        }
    }

    public string FullName
    {
        get
        {
            var s = new StringBuilder();
            if (firstName is not null)
                s.Append(firstName);
            if (middleName is not null)
                s.Append(" " + middleName);
            if (lastName is not null)
                s.Append(" " + lastName);
            string y = s.ToString().Trim();
            return string.IsNullOrEmpty(y) ? Nickname : y;
        }
    }
    [DataMember(EmitDefaultValue = false)]
    public string FirstName
    {
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                value = null;
            if (firstName != value)
            {
                firstName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FullName));
                OnPropertyChanged(nameof(DisplayName));
                if (nickname is null)
                    OnPropertyChanged(nameof(Nickname));
            }
        }
        get => firstName;
    }
    [DataMember(EmitDefaultValue = false)]
    public string MiddleName
    {
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                value = null;
            if (middleName != value)
            {
                middleName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FullName));
                OnPropertyChanged(nameof(DisplayName));
            }
        }
        get => middleName;
    }
    [DataMember(EmitDefaultValue = false)]
    public string LastName
    {
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                value = null;
            if (lastName != value)
            {
                lastName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FullName));
                OnPropertyChanged(nameof(DisplayName));
            }
        }
        get => lastName;
    }

    private string nickname;
    /// <summary>
    /// This is a simplified version of Nickname used so as not to waste storage space on 
    /// </summary>
    [DataMember(EmitDefaultValue = false, Name = "Nickname")]
    [XmlElement("Nickname")]
    public string NicknameForPersistence
    {
        set => nickname = value;
        get => nickname;
    }
    /// <summary>
    /// The actual property, used a run time
    /// </summary>
    [XmlIgnore]
    public string Nickname
    {
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                value = null;
            string s = value;
            if (s is not null)
            {
                s.Trim();
                if (s.Equals(firstName, StringComparison.OrdinalIgnoreCase))
                    s = null;
            }
            if (string.IsNullOrWhiteSpace(nickname) && s is null)
                return;
            if (StringHasChanged(ref nickname, s))
            {
                OnPropertyChanged();
                OnPropertyChanged(nameof(FullName));
                OnPropertyChanged(nameof(DisplayName));
            }
        }
        get => string.IsNullOrWhiteSpace(nickname) ? string.IsNullOrWhiteSpace(firstName) ? "" : firstName : nickname;
    }
    [DataMember(EmitDefaultValue = false)]
    public string Email
    {
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                value = null;
            if (email != value)
            {
                email = value;
                OnPropertyChanged();
            }
        }
        get => email;
    }
    #endregion
    #endregion
}
