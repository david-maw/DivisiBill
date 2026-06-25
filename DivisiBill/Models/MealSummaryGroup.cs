using CommunityToolkit.Mvvm.ComponentModel;
using DivisiBill.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;

namespace DivisiBill.Models;

[DebuggerDisplay("{GetDebuggerDisplay,nq}")]
public partial class MealSummaryGroup : ObservableObject
{
    public MealSummaryGroup() { } // for XAML
    public MealSummaryGroup(MealSummary msParameter)
    {
        VenueName = msParameter.VenueName ?? string.Empty;
        CreationTime = msParameter.CreationTime;
        MealSummaries.CollectionChanged += MealSummaries_CollectionChanged;
        Meal.CurrentMealSummaryChanged += Meal_CurrentMealSummaryChanged;
        MealSummaries.Add(msParameter);
    }
    ~MealSummaryGroup()
    {
        // Unsubscribe from events to avoid memory leaks
        Meal.CurrentMealSummaryChanged -= Meal_CurrentMealSummaryChanged;
        MealSummaries.CollectionChanged -= MealSummaries_CollectionChanged;
    }

    private void MealSummaries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // If the list of MealSummaries changed then the shortened list may have, so regenerate it
        // Cleverer code would update it but it doesn't seem worth the bother for such a short list
        OnPropertyChanged(nameof(FirstMealSummaries)); // just in case it changed
        // Keeps track of count but not CreationTime;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                Count += e.NewItems?.Count ?? 0;
                break;

            case NotifyCollectionChangedAction.Remove:
                Count -= e.OldItems?.Count ?? 0;
                break;

            case NotifyCollectionChangedAction.Replace:
                Count += (e.NewItems?.Count ?? 0) - (e.OldItems?.Count ?? 0);
                break;

            case NotifyCollectionChangedAction.Reset:
                Count = MealSummaries.Count;
                break;
        }
    }
    private void Meal_CurrentMealSummaryChanged(MealSummary? old, MealSummary? newMs) => IsForCurrentMeal = string.Equals(VenueName, newMs?.VenueName, StringComparison.OrdinalIgnoreCase);

    #region Properties
    public string VenueName { get; } = string.Empty;
    /// <summary>
    /// The <see cref="MealSummary.CreationTime"/> of the newest Meal in the group.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApproximateAge))]
    public partial DateTime CreationTime { get; set; } = default;

    public string ApproximateAge => CreationTime.ApproximateAge();

    private const int maxMeals = 9;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    [NotifyPropertyChangedFor(nameof(CountLarge))]
    public partial int Count { get; set; } = 0;

    public bool CountLarge => Count > maxMeals;
    public string CountText => Count <= maxMeals ? $"{Count}" : $"{maxMeals} of {Count}";

    [ObservableProperty]
    public partial bool IsForCurrentMeal { get; set; } = false;
    public int Distance
    {
        get
        {
            var v = Venue.FindVenueByName(VenueName);
            return v is null ? Distances.Unknown : v.SimplifiedDistance;
        }
    }
    public void NotifyDistanceChanged() => OnPropertyChanged(nameof(Distance));
    public ObservableCollection<MealSummary> MealSummaries { get; } = [];
    public ObservableCollection<MealSummary> FirstMealSummaries => new(MealSummaries.Take(maxMeals));
    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = false;
    #endregion
    #region Comparison Functions
    public int CompareTo(MealSummaryGroup otherGroup) => CompareCreationTimeTo(otherGroup);

    /// <summary>
    /// Compare by creation time, latest first. No two creation times should be the same.
    /// </summary>
    /// <param name="otherGroup">the MealSummary to compare the current one with</param>
    /// <returns>+1 if this is later than the parameter, 0 if they are the same (should not happen),-1 if this should precede the parameter</returns>
    public int CompareCreationTimeTo(MealSummaryGroup otherGroup) => otherGroup.CreationTime.CompareTo(CreationTime); // Note that this is inverted because we want newest first;
    public static int CompareCreationTimeTo(MealSummaryGroup thisGroup, MealSummaryGroup otherGroup) => thisGroup.CompareCreationTimeTo(otherGroup);

    /// <summary>
    /// Compare by venue name then creation time newest first
    /// </summary>
    /// <param name="otherGroup">the MealSummary to compare the current one with</param>
    /// <returns>+1 if this would sort later than the parameter, 0 if they are the same (should not happen),-1 if this should precede the parameter</returns>
    public int CompareVenueTo(MealSummaryGroup otherGroup)
    {
        if (Equals(otherGroup))
            return 0;
        if (otherGroup is null)
            return 1;
        int result = VenueName.CompareTo(otherGroup.VenueName);
        if (result == 0)
            result = CompareCreationTimeTo(otherGroup);
        if (result == 0 && Debugger.IsAttached)
            Debugger.Break(); // let the developer know there's a problem
        return result;
    }
    public static int CompareVenueTo(MealSummaryGroup thisGroup, MealSummaryGroup otherGroup) => thisGroup.CompareVenueTo(otherGroup);

    /// <summary>
    /// Compare by distance then venue name then newest first
    /// </summary>
    /// <param name="otherGroup">the MealSummary to compare the current one with</param>
    /// <returns>+1 if this would sort later than the parameter, 0 if they are the same (should not happen),-1 if this should precede the parameter</returns>
    public int CompareDistanceTo(MealSummaryGroup otherGroup)
    {
        if (Equals(otherGroup))
            return 0;
        if (otherGroup is null)
            return 1;
        int result = Distance.CompareTo(otherGroup.Distance);
        if (result == 0)
            result = VenueName.CompareTo(otherGroup.VenueName);
        if (result == 0)
            result = otherGroup.CreationTime.CompareTo(CreationTime);
        if (result == 0 && Debugger.IsAttached)
            Debugger.Break(); // let the developer know there's a problem
        return result;
    }
    public static int CompareDistanceTo(MealSummaryGroup thisGroup, MealSummaryGroup otherGroup) => thisGroup.CompareDistanceTo(otherGroup);
    #endregion
    private string GetDebuggerDisplay => $"{VenueName} ({Count})";
}