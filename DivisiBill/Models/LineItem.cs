using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace DivisiBill.Models;

public static class DinerIdUtilities
{
    public static int ToIndex(this Models.LineItem.DinerID id) => (int)id - 1;
}
[DebuggerDisplay("{ItemName} ({SharesList})")]
public class LineItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    public const byte maxSharers = 10;

    public enum DinerID : byte
    {
        none = 0,
        first = 1,
        limit = maxSharers + 1
    }
    decimal amount = 0;
    string itemName;
    ObservableCollection<bool> sharedBy;
    byte[] extraShares;

    public static uint nextItemNumber = 1;

    public LineItem()
    {
        SetupSharedBy();
        itemName = "Item " + nextItemNumber++;
    }
    public LineItem(LineItem li)
    {
        SetupSharedBy();
        itemName = li.itemName;
        AmountForSharerID = li.AmountForSharerID;
        Amount = li.amount;
        SharesList = li.SharesList;
        Comped = li.Comped;
    }


    void Sharers_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SharedBy));
        OnPropertyChanged(nameof(Sharers));
    }
    /// <summary>
    /// The first DinerID in the list of sharers
    /// </summary>
    [XmlIgnore]
    public DinerID FirstSharer => GetNextSharer();

    /// <summary>
    /// Starting from a given sharer return the next sharer in the list, or DinerID.none if there are no more
    /// </summary>
    /// <param name="init">The DinerID of the last participant to ignore</param>
    /// <returns>The DinerID of the next participant or DinerID.None</returns>
    public DinerID GetNextSharer(DinerID init = DinerID.none)
    {
        DinerID cur = init, next = DinerID.none;
        foreach (var payee in SharedBy.Skip((int)init))
        {
            cur++;
            if (payee)
            {
                next = cur;
                break;
            }
        }
        return next;
    }

    /// <summary>
    /// A one character string representation of the number of participants (aka sharers) sharing this item, returns either:
    /// <list type="table">
    /// <item>
    ///    <term>Blank</term>
    ///    <description>Not shared</description>
    /// </item>
    /// <item>
    ///    <term>A Circled Number</term>
    ///    <description>For example "①", shared by one participant</description>
    /// </item>
    /// <item>
    ///    <term>A Plus Sign ('+')</term>
    ///    <description>Multiple sharers have one share each</description>
    /// </item>
    /// <item>
    ///    <term>An Asterisk ('*')</term>
    ///    <description>MShared unevenly, at least one sharer has multiple shares</description>
    /// </item>
    /// </list>
    /// </summary>
    [XmlIgnore]
    public string Sharers
    {
        get
        {
            uint sharers = 0, inx = 0;
            bool multipleShares = false;
            byte theSharer = 0;
            foreach (bool item in SharedBy)
            {
                if (item)
                {
                    sharers++;
                    if (sharers == 1)
                        theSharer = (byte)inx;
                    if (extraShares[inx] > 0)
                    {
                        multipleShares = true;
                        if (sharers > 1)
                            return "*";
                    }
                }
                inx++;
            }
            if (sharers == 0)
                return "";
            else if (sharers == 1)
                return ((char)('①' + theSharer)).ToString();
            else if (multipleShares)
                return "*";
            else
                return "+";
        }
    }

    /// <summary>
    /// The total number of shares of this item allocated to this sharer.
    /// </summary>
    /// <param name="sharerID">The sharer we are asking about</param>
    /// <returns>The number of shares allocated to this sharer</returns>
    public byte GetShares(DinerID sharerID)
    {
        if (sharerID == DinerID.none)
            return 0;
        else if (sharedBy[(int)(sharerID - 1)])
            return (byte)(1 + extraShares[(int)sharerID - 1]);
        else
            return 0;
    }

    /// <summary>
    /// Sets the total number of shares of this item allocated to this sharer. For historical
    /// reasons this involves the bool list <see cref="SharedBy"/> as well as the byte list 
    /// <see cref="ExtraShares"/>.
    /// </summary>
    /// <param name="sharerID">The sharer we are asking about</param>
    /// <param name="count">The number of shares to allocate to this sharer, should be 9 or less</param>
    public void SetShares(DinerID sharerID, byte count)
    {
        int sharerInx = (int)(sharerID - 1);
        bool extraChanged = false;
        bool sharingChanged;
        if (sharerID == DinerID.none)
            throw new Exception("Bad sharer ID");
        else if (count > 0)
        {
            sharingChanged = !SharedBy[sharerInx];
            SharedBy[sharerInx] = true;
            if (!((extraShares[sharerInx] == 0) ^ (count > 1)))
                extraChanged = true; // Used to be 0 now is not, or vice versa
            extraShares[sharerInx] = (byte)(count - 1);
        }
        else
        {
            sharingChanged = SharedBy[sharerInx];
            SharedBy[sharerInx] = false;
            if (extraShares[sharerInx] > 0)
                extraChanged = true;
            extraShares[sharerInx] = (byte)0;
        }
        if (extraChanged || sharingChanged)
        {
            OnPropertyChanged(nameof(SharedBy));
            OnPropertyChanged(nameof(TotalShares));
            OnPropertyChanged(nameof(TotalSharers));
            OnPropertyChanged(nameof(SharesList));
            OnPropertyChanged(nameof(FilteredAmount));
        }
    }

    /// <summary>
    /// Moves shares from an existing sharer (<see cref="DinerID"/>) to new, overwriting any existing new ones (there should be none).
    /// </summary>
    /// <param name="newSharerID">The sharer that currently holds the shares</param>
    /// <param name="oldSharerID">The sharer to receive the shares</param>
    public void TransferShares(DinerID newSharerID, DinerID oldSharerID)
    {
        int oldInx = oldSharerID.ToIndex();
        int newInx = newSharerID.ToIndex();
        SharedBy[newInx] = SharedBy[oldInx];
        SharedBy[oldInx] = false;
        extraShares[newInx] = extraShares[oldInx];
        extraShares[oldInx] = 0;
        OnPropertyChanged(nameof(SharedBy));
        OnPropertyChanged(nameof(SharesList));
    }

    /// <summary>
    /// Share out a coupon amount based on the overall amount spent by each sharer in the meal it is in.
    /// Share only with current sharers and only if there is more than one
    /// </summary>
    /// <param name="m">the meal to which this coupon belongs</param>
    public void DistributeCouponValue(Meal m)
    {
        // Ensure the this item is really part of the meal
        if (!m.LineItems.Contains(this))
            throw new ArgumentException("invalid meal in DistributeCouponValue");

        // First, make sure there are multiple possible sharers
        if (m.Costs.Count < 2)
            return; // No need to share unless there are 2 or more participants to share it between

        // Figure out who spent something
        var spenders = m.Costs.Where(pc => (pc.ChargedAmount) > 0).ToList();

        // Divide up the shared amount
        var costPerPerson = new decimal[maxSharers]; // The sum of all the items distributed between all the sharers (a subset of participants)
        void assignIndividualCost(PersonCost pc) => costPerPerson[pc.DinerIndex] += pc.ChargedAmount; // Basically, stuff they paid for

        // If there are 0 or 1 existing sharers who spent money divide it between all participants otherwise just between existing sharers (which
        // might be all spending participants)
        if (TotalSharers < 2 || TotalSharers == m.Costs.Count)
            foreach (var pc in spenders) assignIndividualCost(pc); // distribute among all spenders
        else
            foreach (var pc in spenders.Where(pc => GetShares(pc.DinerID) > 0)) assignIndividualCost(pc); // distribute among just existing sharers

        // At this point CostsPerPerson has a total amount entry for each person who purchased something, ignoring any discounts
        var newShares = Meal.CostsToShares(costPerPerson);

        // Transfer the calculated share allocation to this lineitem
        for (DinerID diner = DinerID.first; diner < DinerID.limit; diner++)
        {
            SetShares(diner, newShares[(int)(diner - 1)]);
        }
    }

    /// <summary>
    /// Share the item evenly among participants
    /// </summary>
    /// <param name="costs">the list of costs corresponding to this LineItem</param>
    public void ShareEvenly(IList<PersonCost> costs)
    {
        foreach (PersonCost pc in costs)
        {
            SetShares(pc.DinerID, 1);
        }
    }

    /// <summary>
    /// Reset all the shares for this LineItem to zero, making it unallocated
    /// </summary>
    public void DeallocateShares()
    {
        for (DinerID diner = DinerID.first; diner < DinerID.limit; diner++)
        {
            SetShares(diner, 0);
        }
    }

    /// <summary>
    /// The total number of shares allocated to this item.
    /// </summary>
    [XmlIgnore]
    public int TotalShares
    {
        get
        {
            int howMany = 0;
            for (int i = 0; i < maxSharers; i++)
            {
                if (sharedBy[i])
                    howMany += 1 + ExtraShares[i];
            }
            return howMany;
        }
    }

    /// <summary>
    /// The total number of participants sharing this item.
    /// </summary>
    [XmlIgnore]
    public int TotalSharers
    {
        get
        {
            int howMany = 0;
            for (int i = 0; i < maxSharers; i++)
            {
                if (sharedBy[i])
                    howMany++;
            }
            return howMany;
        }
    }

    /// <summary>
    /// Divides up an item in proportion to the shares each person has, these values are returned with multiple decimal places  
    /// </summary>
    /// <returns>Array of amounts, one entry per possible participant</returns>
    public decimal[] GetAmounts()
    {
        decimal[] amounts = new decimal[maxSharers];
        int howMany = TotalShares;
        if (howMany > 0)
        {
            // Figure out what each person pays toward that item
            decimal eachShare = (decimal)Amount / howMany;
            // Now go though the sharers, allocating an amount to each
            for (int i = 0; (i < maxSharers) && (howMany > 0); i++)
            {
                if (SharedBy[i])
                {
                    int shares = 1 + ExtraShares[i];
                    howMany -= shares;
                    decimal amount = eachShare * shares;
                    amounts[i] += amount;
                }
            } // end loop distributing shares
        }
        return amounts;
    }

    /// <summary>
    /// A list of bool indicating whether a given participant has a share of this item. For more than a single share see <see cref="ExtraShares"/>.
    /// The encoding in XML is handled by <see cref="SharesList"/> 
    /// </summary>
    [XmlIgnore]
    public ObservableCollection<bool> SharedBy
    {
        get
        {
            if (sharedBy is null)
                SetupSharedBy();
            return sharedBy;
        }
    }

    /// <summary>
    /// A list of shares over and above one that each participant has every value set here should have a corresponding setting in <see cref="SharedBy"/>.
    /// The encoding in XML is handled by <see cref="SharesList"/> 
    /// </summary>
    [XmlIgnore]
    public byte[] ExtraShares
    {
        get => extraShares;
        set => extraShares = value;
    }

    /// <summary>
    ///  A string encoding of the number of shares allocated to each participant with a single digit for each <see cref="DinerID"/>.
    ///  Smallest DinerID first.
    /// </summary>
    [XmlAttribute, DefaultValue("")]
    public string SharesList
    {
        get
        {
            DinerID maxDiner;

            for (maxDiner = DinerID.limit - 1; maxDiner > DinerID.none; maxDiner--)
            {
                if (GetShares(maxDiner) > 0)
                    break;
            }
            var sb = new StringBuilder(SharedBy.Count);
            for (DinerID diner = DinerID.first; diner <= maxDiner; diner++)
            {
                sb.Append((char)('0' + GetShares(diner)));
            }
            return sb.ToString();
        }
        set
        {
            int inx = 0;
            for (DinerID diner = DinerID.first; diner < DinerID.limit; diner++)
            {
                if (inx >= value.Length)
                    break;
                byte shares = (byte)(value[inx] - '0');
                if (shares > 0)
                    SetShares(diner, shares);
                inx++;
            }
        }
    }

    /// <summary>
    ///  Initialize the sharing structures.
    /// </summary>
    private void SetupSharedBy()
    {
        sharedBy = new ObservableCollection<bool>();
        for (int j = 0; j < maxSharers; j++)
            sharedBy.Add(false);
        sharedBy.CollectionChanged += Sharers_CollectionChanged;
        if (extraShares is null)
            extraShares = new byte[maxSharers];
    }

    /// <summary>
    ///  Exchange two Sharer IDs.
    /// </summary>
    /// <param name="newID">The old sharer</param>
    /// <param name="oldID">The new sharer</param>
    public void SwapSharerID(DinerID newID, DinerID oldID)
    {
        byte savedShares = GetShares(oldID);
        SetShares(oldID, GetShares(newID));
        SetShares(newID, savedShares);
    }

    /// <summary>
    ///  The name of the item being purchased.
    /// </summary>
    [XmlAttribute]
    public string ItemName
    {
        set
        {
            if (itemName != value)
            {
                itemName = value;
                if (value.StartsWith("Item ")) // Maybe we need to update the next item number
                {
                    if (uint.TryParse(value.Substring(5), out uint itemNumber))
                    {
                        if (nextItemNumber <= itemNumber)
                            nextItemNumber = itemNumber + 1;
                    }
                }
                OnPropertyChanged();
            }
        }
        get => itemName;
    }

    /// <summary>
    /// Find out whether a LineItem has been changed or still has its default value.
    /// </summary>
    /// <returns>true of the LineItem has the default value</returns>
    public bool IsEmpty()
    {
        if (Amount != 0) return false;
        if (TotalSharers != 0) return false;
        string name = ItemName;
        if (string.IsNullOrWhiteSpace(name)) return true;
        if (name.Length < 6) return false;
        return name.StartsWith("Item ") && uint.TryParse(name.Remove(0, 5), out uint _);
    }
    private bool comped = false;

    /// <summary>
    /// Item is free, for example because it was food that was incorrectly prepared.
    /// No tax is due on a comped item, but it still contributes to the tip.
    /// </summary>
    [XmlAttribute, DefaultValue(false)]
    public bool Comped
    {
        get => comped;
        set
        {
            if (value != comped)
            {
                comped = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FilteredAmount));
            }
        }
    }

    /// <summary>
    /// The cost of this item (or negative if this is a discount).
    /// </summary>
    [XmlAttribute, DefaultValue(typeof(decimal), "0")]
    public decimal Amount
    {
        set
        {
            decimal v = Math.Round(value, 2);
            if (amount != v)
            {
                amount = v;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FilteredAmount));
            }
        }
        get => amount;
    }

    /// <summary>
    /// Handy utility property used to show negative amounts in red.
    /// </summary>
    [XmlIgnore]
    public Style RedIfNegative => (FilteredAmount < 0) ? App.RedLabelText : null;

    /// <summary>
    /// Handy utility property used to show negative amounts in red.
    /// </summary>
    [XmlIgnore]
    public string AmountText => Math.Abs(FilteredAmount).ToString("C");

    private DinerID amountForSharerID;

    // Set this to constrain amounts to a particular sharer
    /// <summary>
    /// Used in implementing filtering for a single participant, set to a <see cref="DinerID"/> to filter for that participant
    /// or to <see cref="DinerID.none"/> to stop filtering.
    /// </summary>
    [XmlIgnore]
    public DinerID AmountForSharerID
    {
        set
        {
            if (amountForSharerID != value)
            {
                amountForSharerID = value;
                OnPropertyChanged(nameof(FilteredAmount));
                OnPropertyChanged(nameof(IsSharedByFilter));
            }
        }
        get => amountForSharerID;
    }

    /// <summary>
    /// The amount for a specific participant (or everyone if filtering by participant is off) 
    /// </summary>
    [XmlIgnore]
    public decimal FilteredAmount
    {
        get
        {
            if (amountForSharerID == 0)
                return amount;
            else
            {
                return Math.Round(GetAmounts()[(int)AmountForSharerID - 1], 2);
            }
        }
    }
    /// <summary>
    /// Either there is no current sharer, or this item has one or more shares allocated to the current sharer
    /// </summary>
    [XmlIgnore]
    public bool IsSharedByFilter
    {
        get => AmountForSharerID == DinerID.none || GetShares(AmountForSharerID) != 0;
    }

    /// <summary>
    /// <see cref="INotifyPropertyChanged"/> implementation 
    /// </summary>

    protected virtual void OnPropertyChanged([CallerMemberName] string propChanged = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propChanged));
        if (propChanged.Equals(nameof(FilteredAmount)))
        {
            OnPropertyChanged(nameof(AmountText));
            OnPropertyChanged(nameof(RedIfNegative));
        }
    }
}
