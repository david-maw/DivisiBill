﻿using DivisiBill.Models;
using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace DivisiBill.Services;

public class Archive
{
    public Archive() { }
    public Archive(DateOnly startDate, DateOnly finishDate, bool onlyRelatedParam)
    {
        UserSettings = new UserSettingsClass()
        {
            DefaultTipRate = App.Settings.DefaultTipRate,
            DefaultTaxRate = App.Settings.DefaultTaxRate,
            DefaultTipOnTax = App.Settings.DefaultTipOnTax,
            DefaultTaxOnCoupon = App.Settings.DefaultTaxOnCoupon,
            ShowLineItemsHint = App.Settings.ShowLineItemsHint,
            ShowTotalsHint = App.Settings.ShowTotalsHint,
            ShowVenuesHint = App.Settings.ShowVenuesHint,
            ShowPeopleHint = App.Settings.ShowPeopleHint,
            HadProSubscription = App.Settings.HadProSubscription,
            FakeLocation = App.MyLocation is not null ? new SimpleLocation(App.MyLocation) : null,
            BillsFromDate = startDate > DateOnly.MinValue ? startDate.ToString() : null,
            BillsToDate = finishDate < DateOnly.MaxValue ? finishDate.ToString() : null,
            OnlyRelated = onlyRelatedParam,
        };
        // Make a list of meals one by looping through list of local mealSummaries and creating a meal from each
        Meals = [.. Meal.LocalMealList
            .Where(ms => DateOnly.FromDateTime(ms.CreationTime) >= startDate && DateOnly.FromDateTime(ms.CreationTime) <= finishDate)
            .OrderByDescending(ms => ms.CreationTime)
            .Select(ms => Meal.LoadFromFile(ms))];
        if (Utilities.IsDebug)
        {
            // this is a handy place to check for differences between the old and new DistributeCosts algorithms
            foreach (Meal m in Meals)
                m.CompareCostDistribution();
        }
        if (!onlyRelatedParam)
        {
            // No filtering, just include everything
            Venues = [.. Venue.AllVenues];
            Persons = [.. Person.AllPeople];
            AliasGuids = Person.AliasGuidList;
        }
        else
        {
            Venues = [];
            Persons = [];
            AliasGuids = [];
            // figure out what is used by the meals in the list and just include that
            foreach (var meal in Meals)
            {
                Venue v = Venue.FindVenueByName(meal.VenueName);
                if (v is not null)
                    Venues.Add(v);
                foreach (var pc in meal.Costs.Where(pc => pc.Diner is not null))
                {
                    Persons.Add(pc.Diner);
                    if (pc.PersonGUID != pc.Diner.PersonGUID)
                    {
                        // The item must have used an alias
                        AliasGuids.Add(new GuidMappingEntry() { Key = pc.PersonGUID, Value = pc.Diner.PersonGUID });
                    }
                }
            }
            Venues = [.. Venues.Distinct()];
            Venues.Sort();
            Persons = [.. Persons.Distinct()];
            Persons.Sort();
            AliasGuids = [.. AliasGuids.DistinctBy(a => a.Key)];
            AliasGuids.Sort();
        }
    }
    // The data to archive
    public string Version { get; set; } = "1.3";
    private DateTimeOffset creationTime = DateTimeOffset.Now;
    public string CreationTimeString
    {
        get => creationTime.ToString();
        set => _ = DateTimeOffset.TryParse(value, out creationTime);
    }
    public string TimeName => Utilities.NameFromDateTime(creationTime.LocalDateTime);
    [JsonIgnore]
    public bool DeleteBeforeRestore { get; set; } = false;
    [JsonIgnore]
    public bool OverwriteDuplicates { get; set; } = false;
    public UserSettingsClass UserSettings { get; set; } = null;
    public List<Venue> Venues { get; set; } = null;
    public List<Person> Persons { get; set; } = null;
    public List<GuidMappingEntry> AliasGuids { get; set; } = null;
    public List<Meal> Meals { get; set; } = null;

    public bool ToJsonStream(Stream stream)
    {
        try
        {
            xmlSerializer.Serialize(stream, this);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
    public static Archive FromStream(Stream stream)
    {
        try
        {
            return (Archive)xmlSerializer.Deserialize(stream);
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            return null;
        }
    }

    private static readonly XmlSerializer xmlSerializer = new(typeof(Archive));


    public async Task<bool> RestoreAsync(DateOnly startDate, DateOnly finishDate, bool onlyRelatedParam)
    {
        // Restore each object type except user specifiable defaults because
        // those are restored through a ViewModel and we want to stay ignorant of those.
        // The rest are list object types, restore individual elements but only if an object of the same name does not exist
        // The presumption is that the lists are in whatever order is deemed correct for their item type
        try
        {
            App.IsCloudAllowed = false; // No backups while this is going on
                                        // if we're handling only limited dates reevaluate the list
            if (startDate > DateOnly.MinValue || finishDate < DateOnly.MaxValue)
            {
                // Filter the meal list by date
                Meals = [.. Meals.Where(m => DateOnly.FromDateTime(m.CreationTime) >= startDate && DateOnly.FromDateTime(m.CreationTime) <= finishDate)];
            }
            if (!onlyRelatedParam)
            {
                // filter other lists to limit them to required items
                List<Venue> FilteredVenues = [];
                List<Person> FilteredPersons = [];
                List<GuidMappingEntry> FilteredAliasGuids = [];
                // figure out what is used by the meals in the list and just include that
                foreach (var meal in Meals)
                {
                    Venue v = string.IsNullOrWhiteSpace(meal.VenueName) ? null : Venues.FirstOrDefault(venue => meal.VenueName.Equals(venue.Name));
                    if (v is not null)
                        FilteredVenues.Add(v);
                    foreach (var pc in meal.Costs.Where(pc => pc.PersonGUID != Guid.Empty))
                    {
                        Guid personGuid = pc.PersonGUID;
                        GuidMappingEntry guidMappingEntry = AliasGuids.FirstOrDefault(guidMapping => personGuid.Equals(guidMapping.Key));
                        if (guidMappingEntry is not null)
                        {
                            FilteredAliasGuids.Add(guidMappingEntry);
                            personGuid = guidMappingEntry.Value;
                        }
                        Person person = Persons.FirstOrDefault(person => person.PersonGUID == personGuid);
                        if (person is not null)
                            FilteredPersons.Add(person);
                    }
                }
                Venues = [.. FilteredVenues.Distinct()];
                Venues.Sort();
                Persons = [.. FilteredPersons.Distinct()];
                Persons.Sort();
                AliasGuids = [.. FilteredAliasGuids.DistinctBy(a => a.Key)];
                AliasGuids.Sort();
            }
            // If we're going to clear the current meal do it first so any side effects will be erased later
            if (DeleteBeforeRestore)
            {
                if (Meals is null || Meals.Count == 0)
                    await Meal.LoadFake(new MealSummary()).BecomeCurrentMealAsync();
            }
            if (Venues is not null)
            {
                if (DeleteBeforeRestore)
                    Venue.ForgetAllVenues();
                Venue.MergeVenues(Venues, replace: OverwriteDuplicates);
                await Venue.SaveSettingsAsync();
            }
            if (Persons is not null)
            {
                if (DeleteBeforeRestore)
                    Person.AllPeople.Clear();
                Person.AddPeople(Persons, replace: OverwriteDuplicates);
                if (AliasGuids is not null) // only handled if there are persons
                    Person.AliasGuidList = AliasGuids;
                await Person.SaveSettingsAsync();
            }
            if (Meals is not null)
            {
                if (DeleteBeforeRestore)
                    MealSummary.PermanentlyDeleteLocalMeals(startDate, finishDate);
                Meal m = Meals.Where(m => m.Size > 0).FirstOrDefault();
                if (m is not null)
                {
                    if (m.OldEnoughToBeNewFile)
                        m.Frozen = true;  // Meaning it has been saved and now you have a new copy which must be saved if changed

                    // Restore the first meal in the list (should be the one that was current at the time of the archive) 
                    m.FinalizeSetup();
                    await m.BecomeCurrentMealAsync();
                }
                Meal.AddLocalMeals(Meals, OverwriteDuplicates);
            }
            return true;
        }
        catch (Exception ex)
        {
            ex.ReportCrash();
            return false;
        }
        finally
        {
            App.HandleActivityChanges();
        }
    }
}

public class UserSettingsClass
{
    public int DefaultTipRate { get; set; }
    public double DefaultTaxRate { get; set; }
    public bool DefaultTipOnTax { get; set; }
    public bool DefaultTaxOnCoupon { get; set; }
    public bool ShowLineItemsHint { get; set; }
    public bool ShowTotalsHint { get; set; }
    public bool ShowVenuesHint { get; set; }
    public bool ShowPeopleHint { get; set; }
    public SimpleLocation FakeLocation { get; set; }
    public string BillsFromDate { get; set; }
    public string BillsToDate { get; set; }
    public bool OnlyRelated { get; set; }
    public bool HadProSubscription { get; set; }
}
