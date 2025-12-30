using System.ComponentModel;
using System.Xml.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics;

namespace DivisiBill.Models;

public partial class Meal
{
    /// <summary>
    /// The current tip rate applied to the bill. Changing this will recalculate the tip and reset any manual tip delta.
    /// </summary>
    [ObservableProperty]
    public partial double TipRate { get; set; }
    partial void OnTipRateChanged(double value)
    {
        TipDelta = 0;
        Tip = GetTip();
        MarkAsChanged();
    }

    /// <summary>
    /// The amount to add to (or subtract from) the calculated tip to get the actual currency amount of the tip.
    /// Calculated automatically when a tip amount (rather than a calculated percentage) is specified. It is reset to
    /// 0 whenever a bill is thawed because it's rarely the same (or used at all) on a new bill since fixed percentages
    /// are the norm..
    /// </summary>
    [DefaultValue(typeof(decimal), "0")]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tip))]
    [NotifyPropertyChangedFor(nameof(TotalAmount))]
    public partial decimal TipDelta { get; set; }
    partial void OnTipDeltaChanged(decimal oldValue, decimal newValue)
    {
        Tip += newValue - oldValue;
        MarkAsChanged();
    }

    /// <summary>
    /// Indicates whether tip is calculated on the tax amount as well as the order amount.
    /// </summary>
    [XmlElement(ElementName = "TipOnTax")]
    [ObservableProperty]
    public partial bool TipOnTax { get; set; }
    partial void OnTipOnTaxChanged(bool value)
    {
        Tip = GetTip();
        MarkAsChanged();
        IsDistributed = false;
    }

    /// <summary>
    /// <para>Set if coupon amounts are applied after tax; for example, with a $10 meal and a $1 discount,
    /// tax is normally charged on $9.</para>
    /// <para>If this is set, tax is charged on $10 and the result is then discounted by $1.</para>
    /// </summary>
    [XmlElement(ElementName = "TaxOnDiscount")]
    [ObservableProperty]
    public partial bool IsCouponAfterTax { get; set; }
    partial void OnIsCouponAfterTaxChanged(bool value)
    {
        UpdateAmounts();
        MarkAsChanged();
        IsDistributed = false;
    }

    /// <summary>
    /// The tax rate applied to the taxable portion of the bill.
    /// </summary>
    [ObservableProperty]
    public partial double TaxRate { get; set; }
    partial void OnTaxRateChanged(double value) 
    { 
        TaxDelta = 0;
        Tax = GetTax();
        MarkAsChanged();
    }

    /// <summary>
    /// The amount to add (or subtract) to the calculated tax to get the actual amount charged, set manually,
    /// it should never be more than a few cents.
    /// </summary>
    [DefaultValue(typeof(decimal), "0")]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tax))]
    [NotifyPropertyChangedFor(nameof(TotalAmount))]
    public partial decimal TaxDelta { get; set; }
    partial void OnTaxDeltaChanged(decimal oldValue, decimal newValue)
    {
        Tax += newValue - oldValue;
        MarkAsChanged();
    }

    /// <summary>
    /// The raw tax amount scanned from the bill, used as a reference for calculations.
    /// </summary>
    [DefaultValue(typeof(decimal), "0")]
    [ObservableProperty]
    public partial decimal ScannedTax { get; set; }
    partial void OnScannedTaxChanged(decimal value) => MarkAsChanged();

    /// <summary>
    /// The raw subtotal scanned from the bill, typically matching the printed subtotal.
    /// </summary>
    [ObservableProperty]
    public partial decimal ScannedSubTotal { get; set; }
    partial void OnScannedSubTotalChanged(decimal value) => MarkAsChanged();

    /// <summary>
    /// Indicates whether there is any unallocated amount remaining on the bill.
    /// </summary>
    private bool IsAnyUnallocated => UnallocatedAmount != 0;

    /// <summary>
    /// The bill SubTotal - this should be the same number as is shown on the bill in ScannedSubtotal.
    /// It is the sum of the item amounts ignoring any comped items and perhaps discounts, see <see cref="GetSubTotal"/>.
    /// </summary>
    [XmlIgnore]
    [ObservableProperty]
    public partial decimal SubTotal { get; set; }
    partial void OnSubTotalChanged(decimal value) => UpdateAmounts();

    /// <summary>
    /// The nominal coupon amount applied to the bill, the sum of all the individual coupons
    /// ignoring the fact that coupons may not exceed the amount spent.
    /// </summary>
    public decimal GetRawCouponAmount()
    {
        decimal couponAmount = 0;
        foreach (var item in LineItems)
        {
            if (item.Amount < 0)
                couponAmount -= item.Amount; // Amount is negative, so couponAmount will be positive
        }
        return couponAmount;
    }

    /// <summary>
    /// The actual coupon amount applied to the bill, the sum of all the individual coupons
    /// but no more than the sum of item costs less any comped items (so it is never negative).
    /// Note that this is different from <see cref="GetRawCouponAmount"/> which simply sums the coupons
    /// and does not take note of the amount. Also, the individual coupons may be taxable or not depending
    /// on the value of <see cref="IsCouponAfterTax"/>.
    /// </summary>
    private decimal GetModifiedCouponAmount()
    {
        decimal subTotal = 0;
        decimal couponAmount = 0;
        foreach (var item in LineItems)
        {
            if (item.Amount < 0)
                couponAmount -= item.Amount; // Amount is negative, so couponAmount will be positive
            else if (!item.Comped)
                subTotal += item.Amount;
        }
        return Math.Min(subTotal, couponAmount / (IsCouponAfterTax ? 1 + (decimal)TaxRate : 1));
    }

    /// <summary>
    /// The sum of all the individual coupons for the bill if they are applied before tax.
    /// </summary>
    public decimal GetCouponAmountBeforeTax() => IsCouponAfterTax ? 0 : GetModifiedCouponAmount();

    /// <summary>
    /// The sum of all the individual coupons for the bill if they are applied after tax.
    /// </summary>
    [XmlIgnore]
    [ObservableProperty]
    public partial decimal CouponAmountAfterTax { get; set; }

    /// <summary>
    /// Get the sum of all the individual coupons for the bill if they are applied after tax.
    /// </summary>
    private decimal GetCouponAmountAfterTax() => IsCouponAfterTax ? GetModifiedCouponAmount() : 0;

    /// <summary>
    /// The total amount of all comped (complimentary) items on the bill.
    /// </summary>
    private decimal GetCompedAmount() => LineItems.Where(item => item.Comped).Sum(item => item.Amount);

    /// <summary>
    /// This represents the amount against which Tip for a Meal ought to be calculated.
    /// Negative item amounts are simply discounts and are ignored when calculating a tip
    /// although they are used when calculating tax.
    /// </summary>
    /// <returns>Tip basis from the order items</returns>
    private decimal GetOrderAmount() => LineItems.Where(item => item.Amount > 0).Sum(item => item.Amount);

    /// <summary>
    /// The bill SubTotal - this should be the same number as is shown on the bill in ScannedSubtotal
    /// It is the sum of the item amounts ignoring any comped items.
    /// If discounts are applied after tax they do not affect the subtotal, refer to the <see cref="IsCouponAfterTax"/> property
    /// If discounts are applied before tax (meaning they are taxable, the normal case) they reduce the subtotal but do not affect <see cref="GetTipBasis"/>.
    /// Negative values are not allowed and return zero.
    /// </summary>
    /// <returns>Subtotal of items</returns>
    private decimal GetSubTotal() => Math.Max(0,
        LineItems.Where(item => (item.Amount < 0 && !IsCouponAfterTax)
            || (!item.Comped && item.Amount > 0))
            .Sum(item => item.Amount));

    /// <summary>
    /// The portion of the cost of a meal which is taxable.
    /// </summary>
    private decimal TaxedAmount => GetOrderAmount() - GetCompedAmount() - GetModifiedCouponAmount();

    // Set this to constrain amounts to a particular sharer
    [XmlIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineItems))]
    public partial LineItem.DinerID AmountForSharerID {  get; set; }
    partial void OnAmountForSharerIDChanged(LineItem.DinerID value)
    {
        // Set the value in each individual LineItem - it's easier to get at that way
        foreach (var li in LineItems)
            li.FilterForSharerID = value;
    }

    /// <summary>
    /// Gets or sets the sum of each amount rounded to the nearest integer (which is not necessarily the same as the Total rounded).
    /// </summary>
    [XmlIgnore]
    [ObservableProperty]
    public partial decimal RoundedAmount { get; set; }

    [XmlIgnore]
    [ObservableProperty]
    public partial decimal TotalAmount {  get; set; }
    partial void OnTotalAmountChanged(decimal value) => IsDistributed = false;

    /// <summary>
    /// Calculates the total amount due, including subtotal, tax, and tip, minus any coupon applied after tax.
    /// </summary>
    /// <returns>The total amount to be paid after applying tax, tip, and post-tax coupon deductions.</returns>
    public decimal GetTotalAmount() => SubTotal + Tax + Tip - CouponAmountAfterTax;

    /// <summary>
    /// Calculates the total amount used as the basis for tip calculation, including applicable tax if
    /// <see cref="TipOnTax"/> is specified.
    /// </summary>
    /// <remarks>Comped items are included in the calculation and coupons are ignored when determining the tip basis as
    /// neither affect the work of the server.</remarks>
    /// <returns>The monetary amount on which the tip is calculated.</returns>
    private decimal GetTipBasis() => GetOrderAmount() + (TipOnTax ? Tax : 0);

    [XmlIgnore]
    [ObservableProperty]
    public partial decimal Tip {  get; set; }
    partial void OnTipChanged(decimal value) => TotalAmount = GetTotalAmount();

    private decimal GetTip() => Math.Round(GetTipBasis() * (decimal)TipRate, 2) + TipDelta;

    public void SetRateFromTip(decimal value)
    {
        decimal tipBasis = GetTipBasis();
        if (tipBasis <= 0 || value <= 0)
            return;
        if (Math.Abs(Tip - value) >= 0.01M)
        {
            double newTipRate = SimplestRate(tipBasis, value, App.Settings.DefaultTipRate, 100);
            TipRate = newTipRate;
            TipDelta = value - Math.Round(tipBasis * (decimal)newTipRate, 2);
            Tip = value;
        }
    }

    /// <summary>
    /// Tax amount composed of tax calculated using TaxRate and an added TaxDelta to handle the case where simple arithmetic delivers a value different from the one in use.
    /// </summary>
    [XmlIgnore]
    [ObservableProperty]
    public partial decimal Tax { get; private set; }
    partial void OnTaxChanged(decimal value) 
    {
        if (TipOnTax) Tip = GetTip();
        TotalAmount = GetTotalAmount();
    }

    /// <summary>
    /// The calculated tax amount without the manual TaxDelta adjustment.
    /// </summary>
    public decimal TaxWithoutDelta => Tax - TaxDelta;

    private decimal GetTax()
    {
        // Most states specify simple rounding to do the calculation, and decimal.Round does bankers rounding.
        // The rule is typically: "the calculated tax shall be rounded to a whole cent using a method that rounds up
        // to the next cent whenever the third decimal place is greater than four".
        double tax = (double)TaxedAmount * TaxRate;
        double cents = Math.Floor(tax * 100 + 0.5);
        return (decimal)cents / 100 + TaxDelta;
    }

    public void SetRateFromTax(decimal value)
    {
        if (TaxedAmount <= 0 || value <= 0)
            return;
        if (Math.Abs(Tax - value) >= 0.01M)
        {
            TaxRate = SimplestRate(TaxedAmount, value);
            TaxDelta = value - TaxWithoutDelta;
        }
    }

    /// <summary>
    /// Return a simplified value describing the ratio between two numbers.
    /// We aim for a default rate if it is close enough, otherwise we just pick one.
    /// The definition of "close enough" is one rounded down to the nearest 1/4 % (one part in 400, or 0.0025).
    /// </summary>
    /// <param name="total">The total amount.</param>
    /// <param name="part">The partial amount to be compared to it.</param>
    /// <param name="defaultRate">
    ///     The preferred default ratio between the two numbers, if nothing is provided we use the default tax rate.
    /// </param>
    /// <param name="precision">The granularity of the ratio to return, 100 means 1%, 400 means 1/4% and so on.</param>
    /// <returns>A simplified ratio between the two values.</returns>
    public static double SimplestRate(decimal total, decimal part, double defaultRate = double.NaN, int precision = 400)
    {
        Debug.Assert(precision > 0);
        Debug.Assert(total > 0);
        Debug.Assert(part > 0);
        try
        {
            total = Math.Abs(total);
            part = Math.Abs(part);
            if (double.IsNaN(defaultRate))
                defaultRate = App.Settings.DefaultTaxRate;
            decimal ratio = part / total;
            decimal defaultRateDelta = Math.Abs((decimal)defaultRate - ratio);
            if (defaultRateDelta * precision < 1)
                return defaultRate;
            decimal roundedRatio = Math.Floor(ratio * precision) / precision;
            decimal ratioDelta = ratio - roundedRatio;
            if (ratioDelta > 1M / (precision * 2M))
            {
                roundedRatio += 1M / precision;
                ratioDelta = (1M / precision) - ratioDelta;
            }
            return (ratioDelta < defaultRateDelta) ? (double)roundedRatio : defaultRate;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Calculates the total amount by adding each individual amount rounded if all costs have been allocated.
    /// If any costs remain unallocated, the method rounds the overall total amount.
    /// </summary>
    private decimal GetRoundedAmount()
    {
        decimal accumulatedTotal = 0;
        if (IsAnyUnallocated)
            accumulatedTotal = Math.Round(TotalAmount + 0.001M, 0);
        else
            foreach (var costItem in Costs)
                accumulatedTotal += Math.Round(costItem.Amount + 0.001M, 0);
        return accumulatedTotal;
    }

    /// <summary>
    /// Calculate the various amounts (totals and percentages mostly) derived indirectly or directly from the list of items.
    /// </summary>
    private void UpdateAmounts()
    {
        SubTotal = GetSubTotal();
        Tax = GetTax();
        Tip = GetTip();
        CouponAmountAfterTax = GetCouponAmountAfterTax();
        UnallocatedAmount = GetUnallocatedAmount();
        TotalAmount = GetTotalAmount();
    }
}
