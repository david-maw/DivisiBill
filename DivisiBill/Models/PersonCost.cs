using CommunityToolkit.Mvvm.ComponentModel;
using DivisiBill.Services;
using System.Diagnostics;
using System.Xml.Serialization;

namespace DivisiBill.Models;

[DebuggerDisplay("[{DinerIndex}] {DinerIDText} {Nickname} - {PersonGUID.ToString()}")]
public partial class PersonCost : ObservableObject
{
    // This is the GUID of the diner
    [XmlAttribute]
    public Guid PersonGUID { set; get; }

    /// <summary>
    /// Gets or sets the nickname of the diner associated with this instance.
    /// </summary>
    /// <remarks>If a diner is assigned, this property returns the diner's nickname. Otherwise, it returns the
    /// locally stored nickname value. Setting this property has no effect if a diner is already assigned, as the
    /// diner's nickname takes precedence. If the nickname is null or consists only of whitespace, the property returns
    /// "Unknown" by default.</remarks>
    [XmlAttribute]
    public string Nickname
    {
        set
        {
            if (SetProperty(ref field, value) && Diner is not null)
                Debugger.Break(); // It is useless to set a Nickname if a diner is set because it will be ignored  
        }
        get => Diner is not null ? Diner.Nickname : field.NullIfWhiteSpace() ?? "Unknown";
    }

    /// <summary>
    /// Search AllPeople and find the Person object corresponding to the guid if there is one
    /// </summary>
    /// <returns></returns>
    public bool SetDinerFromGuid()
    {
        if ((Diner is null) && (!PersonGUID.Equals(Guid.Empty)))
            Diner = Person.FindByGuid(PersonGUID);
        return Diner is not null;
    }

    /// <summary>
    /// Gets or sets the person associated with the diner.
    /// </summary>
    /// <remarks>This property is not serialized and is used to track the current person in relation to other
    /// properties such as DinerID and Nickname. Changing this property raises property change notifications for those
    /// related properties.</remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DinerID))]
    [NotifyPropertyChangedFor(nameof(Nickname))]
    [XmlIgnore]
    public partial Person Diner { get; set; }
    partial void OnDinerChanged(Person value)
    {
        if (value is null) // we must be resetting the diner value
            Nickname = null; // make sure no old value has been left lying around
        else
            PersonGUID = value.PersonGUID;
    }

    /// <summary>
    /// Clear all amounts in this object
    /// </summary>
    public void ClearAllAmounts()
    {
        // In Alphabetical order
        Amount = 0;
        PreTaxCouponAmount = 0;
        CompedAmount = 0;
        CouponAmount = 0;
        Discount = 0;
        OrderAmount = 0;
        UnusedCouponAmount = 0;
    }

    /// <summary>
    /// Round all amounts in this object (typically to the number of decimal places in the currency)
    /// </summary>
    public void RoundAllAmounts(int places = 2)
    {
        // In Alphabetical order
        Amount = Math.Round(Amount, places);
        PreTaxCouponAmount = Math.Round(PreTaxCouponAmount, places);
        CompedAmount = Math.Round(CompedAmount, places);
        CouponAmount = Math.Round(CouponAmount, places);
        Discount = Math.Round(Discount, places);
        OrderAmount = Math.Round(OrderAmount, places);
        UnusedCouponAmount = Math.Round(UnusedCouponAmount, places);
    }

    /// <summary>
    /// The coupon amount not yet applied. The sum of any coupon amount this participant got but has not used (by subtracting
    /// from Amount). Initially this is the sum of the coupons allocated to this participant.
    /// </summary>
    [XmlIgnore]
    public decimal UnusedCouponAmount { get; set; }

    /// <summary>
    /// The coupon amount actually subtracted from the participant's total. In a 'normal' bill this starts out exactly the same
    /// as the <see cref="CouponAmount"/> but it can be less if it is a post-tax/taxable coupon in which case it is reduced to allow
    /// tax (<see cref="PreTaxCouponAmount"/>). Additionally, all coupons are subject to reduction in order not to exceed the bill
    /// they are applied to (<see cref="Meal.DistributeCosts"/>.
    /// </summary>
    [XmlIgnore]
    public decimal PreTaxCouponAmount { get; set; }

    /// <summary>
    /// <para>The total value of any coupons assigned to this participant.</para>
    /// Coupons may be before or after tax (see <see cref="Meal.IsCouponAfterTax"/>) and for calculation purposes taxable coupons
    /// (those in bills with <see cref="Meal.IsCouponAfterTax"/> set) make a reduced contribution to Amount so that once tax is added to them
    /// the original amount is restored. <see cref="CouponAmount"/> contains the user specified coupon vale not the pre-tax calculated 
    /// one in <see cref="PreTaxCouponAmount"/>.
    /// </summary>
    [ObservableProperty]
    [XmlIgnore]
    public partial decimal CouponAmount { get; set; }

    /// <summary>
    /// The sum of any comped items this participant got, and any coupons (possibly reduced if they are taxable).
    /// Coupon amounts (not reduced) and comped items are also tracked separately.
    /// </summary>
    [ObservableProperty]
    [XmlIgnore]
    public partial decimal Discount { get; set; }

    /// <summary>
    /// The amount actually charged - the order amount minus anything that was comped and excluding any coupons.
    /// This is the tax basis for this participant.
    /// </summary>
    public decimal ChargedAmount => OrderAmount - CompedAmount;

    /// <summary>
    /// The sum of this participant's shares in comped items.
    /// </summary>
    [ObservableProperty]
    [XmlIgnore]
    public partial decimal CompedAmount { get; set; }

    /// <summary>
    /// The sum of shares in any items this participant ordered, including comped items, excluding coupons
    /// </summary>
    [ObservableProperty]
    [XmlIgnore]
    public partial decimal OrderAmount { get; set; }

    /// <summary>
    /// The amount this participant will pay, so it has any coupons subtracted, comped items ignored and a 
    /// fair share of <see cref="Meal.Tip"/> and <see cref="Meal.Tax"/> added.
    /// </summary>
    [ObservableProperty]
    [XmlIgnore]
    public partial decimal Amount { get; set; }

    /// <summary>
    /// Gets or sets an identifier for a diner.
    /// </summary>
    /// <remarks>This property is used to a specific diner in the context of an order.
    /// Ensure that the DinerID is valid and corresponds to an existing diner in the system. This is persisted
    /// but for historical reasons it is under the name DinerIndex (see <see cref="DinerIndexStored"/>).</remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DinerIndex))]
    [NotifyPropertyChangedFor(nameof(DinerIDText))]
    [XmlIgnore]
    public partial LineItem.DinerID DinerID { get; set; }
    partial void OnDinerIDChanged(LineItem.DinerID value) => DinerIndexStored = (uint)value;

    // This is used to persist the DinerID value, to stay compatible with older stored meals.
    // So the thing called DinerIndex in the persisted XML is actually the DinerID value
    [XmlAttribute(AttributeName = "DinerIndex")]
    [ObservableProperty]
    public partial uint DinerIndexStored { get; set; }
    partial void OnDinerIndexStoredChanged(uint value) => DinerID = (LineItem.DinerID)value;

    // Diner ID values start at 1, this starts at 0 and is used as an array index usually
    [XmlIgnore]
    public byte DinerIndex => (byte)((int)DinerID - 1);

    [XmlIgnore]
    public string DinerIDText => ((char)('①' + DinerIndex)).ToString();

    public void SwapDinerID(PersonCost pc) => (pc.DinerID, DinerID) = (DinerID, pc.DinerID);
}
