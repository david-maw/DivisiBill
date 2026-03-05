using CommunityToolkit.Mvvm.ComponentModel;

namespace DivisiBill.ViewModels;

public partial class AdjustViewModel(decimal subTotal, double taxRate, decimal taxDelta, decimal postTaxDiscount) : ObservableObject
{
    [ObservableProperty]
    public partial decimal SubTotal { get; set; } = subTotal;

    [ObservableProperty]
    public partial decimal AdjustmentAmount { get; set; } = 0;

    [ObservableProperty]
    public partial decimal Tax { get; set; } = subTotal * (decimal)taxRate + taxDelta;

    public decimal PostTaxDiscount { get; } = postTaxDiscount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tax))]
    [NotifyPropertyChangedFor(nameof(AdjustmentAmount))]
    public partial decimal TargetAmount { get; set; } = subTotal * (1 + (decimal)taxRate) + taxDelta - postTaxDiscount;
    partial void OnTargetAmountChanged(decimal value)
    {
        decimal pretax = (value - taxDelta + PostTaxDiscount) / (1 + (decimal)taxRate);
        AdjustmentAmount = pretax - SubTotal;
        Tax = (SubTotal + AdjustmentAmount) * (decimal)taxRate + taxDelta;
    }
}
