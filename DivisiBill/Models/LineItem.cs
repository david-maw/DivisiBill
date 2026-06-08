using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace DivisiBill.Models;

[DebuggerDisplay("{ItemName} ({SharesList})")]
public partial class LineItem : ObservableObject
{
    /// <summary>
    /// Maximum supported sharers for a single LineItem.
    /// </summary>
    public const byte maxSharers = 10;

    /// <summary>
    /// Identifies a diner by a small integer value. Values are 1-based for the first diner;
    /// <see cref="none"/> represents no diner and <see cref="limit"/> is one past the highest valid value.
    /// </summary>
    public enum DinerID : byte
    {
        /// <summary>
        /// No diner selected / sentinel value.
        /// </summary>
        none = 0,
        /// <summary>
        /// First diner (1-based index).
        /// </summary>
        first = 1,
        /// <summary>
        /// One beyond the maximum diner id (used for iteration limits).
        /// </summary>
        limit = maxSharers + 1
    }

    /// <summary>
    /// Backing collection tracking whether each potential diner shares this item.
    /// </summary>
    private ObservableCollection<bool> sharedBy;

    /// <summary>
    /// Next default item number used when creating unnamed items.
    /// </summary>
    public static int NextItemNumber { get; set; } = 1;

    /// <summary>
    /// Initializes a new instance of <see cref="LineItem"/> with default values and a default item name.
    /// </summary>
    public LineItem() => SetupSharedBy();

    /// <summary>
    /// Initializes a new instance of <see cref="LineItem"/> copying values from the provided <paramref name="li"/>.
    /// </summary>
    /// <param name="li">Source <see cref="LineItem"/> to copy values from.</param>
    public LineItem(LineItem li)
    {
        SetupSharedBy();
        ItemName = li.ItemName;
        FilterForSharerID = li.FilterForSharerID;
        Amount = li.Amount;
        SharesList = li.SharesList;
        Comped = li.Comped;
    }

    /// <summary>
    /// Handles changes to the sharers collection and raises property change notifications for dependent properties.
    /// </summary>
    /// <param name="sender">The collection that changed.</param>
    /// <param name="e">Event data describing the change.</param>
    private void Sharers_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
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
        foreach (bool payee in SharedBy.Skip((int)init))
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
    ///    <description>Multiple sharers have one share each, the rest, zero</description>
    /// </item>
    /// <item>
    ///    <term>An Asterisk ('*')</term>
    ///    <description>Shared unevenly, at least one sharer has multiple shares</description>
    /// </item>
    /// </list>
    /// </summary>
    [XmlIgnore]
    public string Sharers
    {
        get
        {
            int sharers = 0, inx = 0;
            bool multipleShares = false;
            int theSharer = 0;
            foreach (bool item in SharedBy)
            {
                if (item)
                {
                    sharers++;
                    if (sharers == 1)
                        theSharer = inx;
                    if (ExtraShares[inx] > 0)
                    {
                        multipleShares = true;
                        if (sharers > 1)
                            return "*";
                    }
                }
                inx++;
            }
            return sharers == 0 ? ""
                : sharers == 1
                    ? ((char)('①' + theSharer)).ToString()
                    : multipleShares ? "*" : "+";
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
        int index = sharerID.ToIndex();
        int shares = index < 0 || index >= SharedBy.Count
            ? 0
            : SharedBy[index]
                ? (1 + ExtraShares[index])
                : 0;
        return (byte)shares;
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
        if (sharerID == DinerID.none)
            throw new Exception("Bad sharer ID");

        int sharerInx = sharerID.ToIndex();
        bool extraChanged;
        bool sharingChanged;

        if (count > 0)
        {
            sharingChanged = !SharedBy[sharerInx];
            SharedBy[sharerInx] = true;
            extraChanged = ExtraShares[sharerInx] != (count - 1);
            ExtraShares[sharerInx] = (byte)(count - 1);
        }
        else
        {
            sharingChanged = SharedBy[sharerInx];
            SharedBy[sharerInx] = false;
            extraChanged = ExtraShares[sharerInx] > 0;
            ExtraShares[sharerInx] = 0;
        }
        if (extraChanged || sharingChanged)
        {
            OnPropertyChanged(nameof(SharedBy));
            OnPropertyChanged(nameof(TotalShares));
            OnPropertyChanged(nameof(TotalSharers));
            OnPropertyChanged(nameof(SharesList));
            OnPropertyChanged(nameof(FilteredAmount));
            OnPropertyChanged(nameof(Sharers));
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
        ExtraShares[newInx] = ExtraShares[oldInx];
        ExtraShares[oldInx] = 0;
        OnPropertyChanged(nameof(SharedBy));
        OnPropertyChanged(nameof(SharesList));
    }

    /// <summary>
    /// Indicates how shares are distributed among sharers.
    /// </summary>
    public enum SharingType
    {
        /// <summary>
        /// All sharers have exactly one share each (total sharers == total shares).
        /// </summary>
        Even,
        /// <summary>
        /// At least one sharer has multiple shares.
        /// </summary>
        Uneven,
        /// <summary>
        /// No sharers are allocated for this item.
        /// </summary>
        None
    }

    /// <summary>
    /// Determines the <see cref="SharingType"/> for this item.
    /// </summary>
    /// <returns>The sharing type: <see cref="SharingType.None"/>, <see cref="SharingType.Even"/> or <see cref="SharingType.Uneven"/>.</returns>
    public SharingType GetSharingType() => (TotalSharers == 0) ? SharingType.None : (TotalSharers == TotalShares) ? SharingType.Even : SharingType.Uneven;

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
        var spenders = m.Costs.Where(pc => pc.ChargedAmount > 0).ToList();

        // Divide up the shared amount
        decimal[] costPerPerson = new decimal[maxSharers]; // The sum of all the items distributed between all the sharers (a subset of participants)
        void assignIndividualCost(PersonCost pc) => costPerPerson[pc.DinerIndex] += pc.ChargedAmount; // Basically, stuff they paid for

        // If there are 0 or 1 existing sharers who spent money divide it between all participants otherwise just between existing sharers (which
        // might be all spending participants)
        if (TotalSharers < 2 || TotalSharers == m.Costs.Count)
            foreach (PersonCost pc in spenders)
                assignIndividualCost(pc); // distribute among all spenders
        else
            foreach (PersonCost pc in spenders.Where(pc => GetShares(pc.DinerID) > 0))
                assignIndividualCost(pc); // distribute among just existing sharers

        // At this point CostsPerPerson has a total amount entry for each person who purchased something, ignoring any discounts
        byte[] newShares = Meal.CostsToShares(costPerPerson);

        // Transfer the calculated share allocation to this LineItem
        for (DinerID diner = DinerID.first; diner < DinerID.limit; diner++)
        {
            SetShares(diner, newShares[diner.ToIndex()]);
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
            decimal eachShare = Amount / howMany;
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
    [ObservableProperty]
    public partial byte[] ExtraShares { get; set; } = new byte[maxSharers];

    /// <summary>
    ///  A string encoding of the number of shares allocated to each participant with a single digit for each <see cref="DinerID"/>.
    ///  Smallest DinerID first.
    /// </summary>
    [XmlAttribute, DefaultValue("")]
    public string SharesList
    {
        get
        {
            DinerID maxDiner = GetMaxDiner();
            StringBuilder sb = new(SharedBy.Count);
            for (DinerID diner = DinerID.first; diner <= maxDiner; diner++)
            {
                sb.Append((char)('0' + GetShares(diner)));
            }
            return sb.ToString();
        }
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                DeallocateShares();
                return;
            }
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
    /// Retrieves the highest DinerID that has a positive share count. It iterates from the maximum limit down to find
    /// the first valid DinerID.
    /// </summary>
    /// <returns>Returns the maximum DinerID with shares greater than zero.</returns>
    public DinerID GetMaxDiner()
    {
        DinerID maxDiner;

        for (maxDiner = DinerID.limit - 1; maxDiner > DinerID.none; maxDiner--)
        {
            if (GetShares(maxDiner) > 0)
                break;
        }

        return maxDiner;
    }

    /// <summary>
    ///  Initialize the sharing structures.
    /// </summary>
    private void SetupSharedBy()
    {
        sharedBy = new ObservableCollection<bool>(Enumerable.Repeat(false, maxSharers));
        sharedBy.CollectionChanged += Sharers_CollectionChanged;
        ExtraShares ??= new byte[maxSharers];
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
    [ObservableProperty]
    public partial string ItemName { get; set; }

    /// <summary>
    /// Called when <see cref="ItemName"/> changes. Attempts to update <see cref="NextItemNumber"/> based on the new name.
    /// </summary>
    /// <param name="value">The new item name.</param>
    partial void OnItemNameChanged(string value)
    {
        TrySetNextItemNumberFromName(value);
    }

    /// <summary>
    /// Attempts to update the next available item number based on the specified item name.
    /// </summary>
    /// <remarks>If the item name corresponds to a default item number greater than the current next item
    /// number, the next item number is advanced to one greater than the default. This method does not return a value
    /// and does not indicate whether an update occurred.</remarks>
    /// <param name="value">The name of the item used to determine the default item number.</param>
    private static void TrySetNextItemNumberFromName(string value)
    {
        int defaultItemNumber = DefaultItemNumber(value);
        if (defaultItemNumber >= NextItemNumber)
            NextItemNumber = defaultItemNumber + 1;
    }
    /// <summary>
    /// Extracts the item number from a string that begins with the prefix "Item ".
    /// </summary>
    /// <param name="value">The string to parse for an item number. Must not be null.</param>
    /// <returns>The item number as an integer if the input starts with "Item " followed by a valid integer; otherwise, -1.</returns>
    private static int DefaultItemNumber(string value) => !string.IsNullOrEmpty(value) && value.StartsWith("Item ") && value.Length > 5 && int.TryParse(value[5..], out int itemNumber)
        ? itemNumber
        : -1;

    /// <summary>
    /// Ensures that the item has a valid name by assigning a default name if none is set.
    /// </summary>
    /// <remarks>If the current item name is null, empty, or consists only of white-space characters, this
    /// method assigns a default name in the format "Item N", where N is the next available item number. This method is
    /// typically used to guarantee that each new item has a non-empty, user-visible name.</remarks>
    public void EnsureItemName()
    {
        if (string.IsNullOrWhiteSpace(ItemName))
        {
            ItemName = $"Item {NextItemNumber}";
        }
    }

    /// <summary>
    /// Item is free, for example because it was food that was incorrectly prepared.
    /// No tax is due on a comped item, but it still contributes to the tip.
    /// </summary>
    [XmlAttribute]
    [DefaultValue(false)]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSharedByFilter))]
    public partial bool Comped { get; set; } = false;

    /// <summary>
    /// The cost of this item (or negative if this is a discount).
    /// </summary>
    [XmlAttribute, DefaultValue(typeof(decimal), "0")]
    public decimal Amount
    {
        set
        {
            decimal v = Math.Round(value, 2);
            if (field != v)
            {
                field = v;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FilteredAmount));
            }
        }
        get;
    } = 0;

    /// <summary>
    /// Gets or sets the identifier of the diner to use as a filter for shared items.
    /// <remarks>Changing this property raises property change notifications for the FilteredAmount and
    /// IsSharedByFilter properties. Use this property to restrict calculations or views to items associated with a
    /// specific diner.
    /// Used in implementing filtering (which limits items to those including a specific participant and shows the amount allocated 
    /// to that participant. Set to a <see cref="DinerID"/> to filter for that participant or to <see cref="DinerID.none"/> to stop filtering.
    /// </remarks>
    /// </summary>
    [XmlIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredAmount))]
    [NotifyPropertyChangedFor(nameof(IsSharedByFilter))]
    public partial DinerID FilterForSharerID { get; set; } = DinerID.none;

    /// <summary>
    /// Gets a value indicating whether no filter is applied to the sharer identifier.
    /// </summary>
    [XmlIgnore]
    public bool IsUnfiltered => FilterForSharerID == DinerID.none;

    /// <summary>
    /// The amount for a specific participant (or everyone if filtering by participant is off) 
    /// </summary>
    [XmlIgnore]
    public decimal FilteredAmount => IsUnfiltered ? Amount : Math.Round(GetAmounts()[FilterForSharerID.ToIndex()], 2);
    /// <summary>
    /// Either there is no current sharer, or this item has one or more shares allocated to the current sharer. This is used
    /// to filter the list of LineItem objects dor just the filtered sharer, or everyone.
    /// </summary>
    [XmlIgnore]
    public bool IsSharedByFilter => IsUnfiltered || GetShares(FilterForSharerID) != 0;
}

public static class DinerIdUtilities
{
    public static int ToIndex(this LineItem.DinerID id) => (int)id - 1;
}
