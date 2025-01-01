using DivisiBill.Models;
using DivisiBill.Services;
using System.Collections.ObjectModel;

namespace DivisiBill.ViewModels;

[QueryProperty(nameof(Summary), "Summary")]
[QueryProperty(nameof(CurrentMeal), "Meal")]
[QueryProperty(nameof(ShowStorage), "ShowStorage")]
public class MealSummaryViewModel // Not inherited from BaseNotifypropertyChanged because these are readonly values and so need not be observable
{
    public MealSummaryViewModel() { }

    private Meal m;
    private MealSummary ms = new();

    public MealSummary Summary
    {
        get => ms;
        set
        {
            if (CurrentMeal is null)
                ms = value;
        }
    }
    public Meal CurrentMeal
    {
        get => m;
        set
        {
            m = value;
            ms = value?.Summary;
        }
    }
    public string VenueName => ms.VenueName;
    public DateTime LastChangeTime => ms.LastChangeTime;
    public string ApproximateChangeAge => Utilities.ApproximateAge(LastChangeTime);
    public bool IsLastChangeTimeDifferent => !Utilities.WithinOneSecond(CreationTime, LastChangeTime);
    public string LastChangeTimeText => ms.GetLastChangeString();
    public DateTime CreationTime => ms.CreationTime;
    public string Id => ms.Id;
    public String ApproximateAge => ms.ApproximateAge;
    public Decimal RoundedAmount => ms.RoundedAmount;
    public ObservableCollection<PersonCost> Costs => m?.Costs;
    public int LineItemCount => m?.LineItems?.Count ?? 0;
    public bool HasImage => ms.HasImage;
    public bool HasDeletedImage => ms.HasDeletedImage;
    public bool IsBad => m is not null && m.Size < 0;
    public string ErrorMessage => IsBad ? m?.CreationReason : string.Empty;
    public string FileName => ms.FileName;
    public decimal UnallocatedAmount => m is null ? 0 : m.UnallocatedAmount;
    public bool IsAnyUnallocated => UnallocatedAmount != 0;
    public bool ShowStorage { get; set; }
    public bool IsLocal => ms.IsLocal;
    public bool IsRemote => ms.IsRemote;
    public bool IsFake => ms.IsFake;
    public bool IsForCurrentMeal => ms.IsForCurrentMeal;
    /// <summary>
    /// Delete a local or remote stored meal but never both 
    /// </summary>
    /// <returns></returns>
    public async Task DeleteMeal()
    {
        if (ms.IsLocal)
            await m.Summary.DeleteAsync(doLocal: true, doRemote: false);
        else if (ms.IsRemote)
            await m.Summary.DeleteAsync(doLocal: false, doRemote: true);
    }
}
