using System.ComponentModel;

namespace DivisiBill.InAppBilling;

[AttributeUsage(AttributeTargets.All)]
[EditorBrowsable(EditorBrowsableState.Never)]
internal sealed class PreserveAttribute : Attribute
{
    public bool AllMembers;
    public bool Conditional;

    public PreserveAttribute(bool allMembers, bool conditional)
    {
        AllMembers = allMembers;
        Conditional = conditional;
    }

    public PreserveAttribute()
    {
    }
}
