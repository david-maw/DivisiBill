using DivisiBill.Models;
using DivisiBill.Services;
using System.Collections.ObjectModel;

namespace DivisiBill.ViewModels;

public class MealSummaryViewModel // Not inherited from BaseNotifypropertyChanged because these are readonly values and so need not be observable
{
    readonly Meal m;
    readonly MealSummary ms;
    public MealSummaryViewModel(MealSummary item, Meal mealParameter = null, bool showStorageParameter = true)
    {
        m = mealParameter;

        ms = m?.Summary ?? item; // Use m.Summary if there is one
        ShowStorage = showStorageParameter; 
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
    public Meal CurrentMeal => m;
    public bool HasImage => ms.HasImage;
    public bool IsBad => m is not null && m.Size < 0;
    public string ErrorMessage => IsBad ? m?.CreationReason : string.Empty;
    public string FileName => ms.FileName;
    public decimal UnallocatedAmount => m is null ? 0 : m.UnallocatedAmount;
    public bool IsAnyUnallocated => UnallocatedAmount != 0;
    public bool ShowStorage { get; private set; }
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
            await m.Summary.DeleteAsync(doLocal:true, doRemote:false);
        else if (ms.IsRemote)
            await m.Summary.DeleteAsync(doLocal: false, doRemote: true);
    }
}
