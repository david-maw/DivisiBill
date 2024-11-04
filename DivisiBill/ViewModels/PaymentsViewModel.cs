namespace DivisiBill.ViewModels;

public record class PaymentsViewModel(decimal Charge, decimal RoundedAmount, string Nickname, decimal NicknameOwed, decimal Unallocated)
{
    public bool IsAnyUnallocated => Unallocated != 0;
    public bool IsPersonal => !String.IsNullOrWhiteSpace(Nickname);
    public decimal AdjustedTip => RoundedAmount - Charge;
}
