using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DivisiBill.ViewModels;

public partial class AdjustViewModel(decimal subTotal, double taxRate, decimal postTaxDiscount) : ObservableObject
{
    [ObservableProperty]
    public partial decimal SubTotal { get; set; } = subTotal;

    [ObservableProperty]
    public partial decimal AdjustmentAmount { get; set; } = 0;

    [ObservableProperty]
    public partial decimal Tax { get; set; } = subTotal * (decimal)taxRate;

    public decimal PostTaxDiscount { get; } = postTaxDiscount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tax))]
    [NotifyPropertyChangedFor(nameof(AdjustmentAmount))]
    public partial decimal TargetAmount { get; set; }
    partial void OnTargetAmountChanged(decimal value)
    {
        decimal pretax = (value + PostTaxDiscount) / (1 + (decimal)taxRate);
        AdjustmentAmount = pretax - SubTotal;
        Tax = (SubTotal + AdjustmentAmount) * (decimal)taxRate;
    }
    #region TargetAmountString
    private void LoadTargetAmountString() => TargetAmountString = string.Format("{0:0.00}", TargetAmount);
    [RelayCommand]
    public void UnloadTargetAmountString()
    {
        if (TargetAmountStringIsValid)
        {
            TargetAmount = decimal.Parse(TargetAmountString);
            LoadTargetAmountString();
        }
    }

    [ObservableProperty]
    public partial string TargetAmountString { get; set; } = string.Format("{0:0.00}", subTotal * (1 + (decimal)taxRate) - postTaxDiscount);
    public bool TargetAmountStringIsValid { get; set; }
    #endregion

}
