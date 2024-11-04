using System.ComponentModel;
using System.Xml.Serialization;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace DivisiBill.Models;

[DebuggerDisplay("[{DinerIndex}] {Nickname} - {PersonGUID.ToString()}")]
public class PersonCost : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    Person diner;
    LineItem.DinerID dinerID; // defined per bill
    Guid personGUID; // in case there's no diner
    string nickname; // in case there's no diner

    // This is the GUID of the diner
    [XmlAttribute]
    public Guid PersonGUID
    {
        set => personGUID = value;
        get => personGUID;
    }


    // This is the name of the diner, or a default
    [XmlAttribute]
    public string Nickname
    {
        set
        {
            if (string.IsNullOrEmpty(nickname) || !nickname.Equals(value))
            {
                if (Debugger.IsAttached && diner is not null)
                    Debugger.Break(); // It is useless to set a Nickname if a diner is set because it will be ignored  
                nickname = value;
                OnPropertyChanged();
            }
        }
        get => (diner is null) ? (string.IsNullOrWhiteSpace(nickname) ? "Unknown" : nickname) : diner.Nickname;
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
    // Note that this is not a data member
    [XmlIgnore]
    public Person Diner
    {
        set
        {
            if (diner != value)
            {
                if (value is null) // we must be resetting the diner value
                    nickname = null; // make sure no old value has been left lying around
                else
                {
                    diner = value;
                    PersonGUID = value.PersonGUID;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(DinerID));
                OnPropertyChanged(nameof(Nickname)); // No need to assign this as the presence of a diner value overrides it 
            }
        }
        get => diner;
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
        Amount               = Math.Round(Amount               , places);
        PreTaxCouponAmount = Math.Round(PreTaxCouponAmount , places);
        CompedAmount         = Math.Round(CompedAmount         , places);
        CouponAmount         = Math.Round(CouponAmount         , places);
        Discount             = Math.Round(Discount             , places);
        OrderAmount          = Math.Round(OrderAmount          , places);
        UnusedCouponAmount   = Math.Round(UnusedCouponAmount   , places);
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
    /// they are applied to (<see cref="Meal.DistributeCosts"/> with the remainder reported as an unallocated amount.
    /// </summary>
    [XmlIgnore]
    public decimal PreTaxCouponAmount { get; set; }
    
    private decimal couponAmount;
    /// <summary>
    /// <para>The total value of any coupons assigned to this participant.</para>
    /// Coupons may be before or after tax (<see cref="Meal.IsCouponAfterTax"/>) and for calculation purposes taxable coupons
    /// (those in bills with <see cref="Meal.IsCouponAfterTax"/> set) make a reduced contribution to Amount so that once tax is added to them
    /// the original amount is restored. This field contains the user specified coupon vale not the pre-tax calculated 
    /// one <see cref="PreTaxCouponAmount"/>.
    /// </summary>
    [XmlIgnore]
    public decimal CouponAmount
    {
        set
        {
            if (couponAmount != value)
            {
                couponAmount = value;
                OnPropertyChanged();
            }
        }
        get => couponAmount;
    }

    private decimal discount;
    /// <summary>
    /// The sum of any comped items this participant got, and any coupons (possibly reduced if they are taxable).
    /// Coupon amounts (not reduced) and comped items are also tracked separately.
    /// </summary>
    [XmlIgnore]
    public decimal Discount
    {
        set
        {
            if (discount != value)
            {
                discount = value;
                OnPropertyChanged();
            }
        }
        get => discount;
    }

    /// <summary>
    /// The amount actually charged - the order amount minus anything that was comped and excluding any coupons.
    /// This is the tax basis for this participant.
    /// </summary>
    public decimal ChargedAmount => OrderAmount - CompedAmount;
    
    private decimal compedAmount;
    /// <summary>
    /// The sum of this participant's shares in comped items.
    /// </summary>
    [XmlIgnore]
    public decimal CompedAmount
    {
        set
        {
            if (compedAmount != value)
            {
                compedAmount = value;
                OnPropertyChanged();
            }
        }
        get => compedAmount;
    }

    private decimal orderAmount;
    /// <summary>
    /// The sum of shares in any items this participant ordered, including comped items, excluding coupons
    /// </summary>
    [XmlIgnore]
    public decimal OrderAmount
    {
        set
        {
            if (orderAmount != value)
            {
                orderAmount = value;
                OnPropertyChanged();
            }
        }
        get => orderAmount;
    }

    private decimal amount;
    /// <summary>
    /// The amount this participant will pay, so it has any coupons subtracted, comped items ignored and a 
    /// fair share of <see cref="Meal.Tip"/> and <see cref="Meal.Tax"/> added.
    /// </summary>
    [XmlIgnore]
    public decimal Amount
    {
        set
        {
            if (amount != value)
            {
                amount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AmountText));
            }
        }
        get => amount;
    }
    [XmlIgnore]
    public string AmountText => Math.Abs(amount).ToString("C");

    [XmlIgnore]
    public LineItem.DinerID DinerID
    {
        set
        {
            if (dinerID != value)
            {
                dinerID = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DinerIndex));
            }
        }
        get => dinerID;
    }

    // This is used to persist the DinerID value, to stay compatible with older stored meals.
    // So the thing called DinerIndex in the persisted XML is actually the DinerID value
    [XmlAttribute(AttributeName = "DinerIndex")]
    public uint DinerIndexStored
    {
        set
        {
            if ((uint)dinerID != value)
            {
                dinerID = (LineItem.DinerID)value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DinerID));
            }
        }
        get => (uint)dinerID;
    }

    // Diner ID values start at 1, this starts at 0 and is used as an array index usually
    [XmlIgnore]
    public byte DinerIndex
    {
        get
        {
            return (byte)((int)dinerID - 1);
        }
    }

    [XmlIgnore]
    public string DinerIDText
    {
        get
        {
            return ((char)('①' + DinerIndex)).ToString();
        }
    }
    protected virtual void OnPropertyChanged([CallerMemberName] string propChanged = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propChanged));
    public void SwapDinerID(PersonCost pc)
    {
       var temp = DinerID;
       DinerID = pc.DinerID;
       pc.DinerID = temp;
    }
}
