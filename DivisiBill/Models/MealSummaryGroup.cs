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
        VenueName = msParameter.VenueName;
        CreationTime = msParameter.CreationTime;
        MealSummaries.CollectionChanged += MealSummaries_CollectionChanged;
        MealSummaries.Add(msParameter);
    }
    ~MealSummaryGroup()
    {
        MealSummaries.CollectionChanged -= MealSummaries_CollectionChanged;
    }

    private void MealSummaries_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        // Keeps track of count but not CreationTime;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                Count += e.NewItems.Count;
                break;

            case NotifyCollectionChangedAction.Remove:
                Count -= e.OldItems.Count;
                break;

            case NotifyCollectionChangedAction.Replace:
                Count += e.NewItems.Count - e.OldItems.Count;
                break;

            case NotifyCollectionChangedAction.Reset:
                Count = MealSummaries.Count;
                break;
        }
    }

    #region Properties
    public string VenueName { get; }
    /// <summary>
    /// The <see cref="MealSummary.CreationTime"/> of the newest Meal in the group.
    /// </summary>
    [ObservableProperty]
    public partial DateTime CreationTime { get; set; } = default;
    partial void OnCreationTimeChanged(DateTime value)
    {
        OnPropertyChanged(nameof(ApproximateAge));
    }

    public string ApproximateAge => CreationTime.ApproximateAge();
    [ObservableProperty]
    public partial int Count { get; set; } = 0;
    public int Distance
    {
        get
        {
            Venue v = Venue.FindVenueByName(VenueName);
            return v is null ? Distances.Unknown : v.SimplifiedDistance;
        }
    }
    public ObservableCollection<MealSummary> MealSummaries { get; } = [];
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
        if (this.Equals(otherGroup)) return 0;
        if (otherGroup is null) return 1;
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
        if (this.Equals(otherGroup)) return 0;
        if (otherGroup is null) return 1;
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