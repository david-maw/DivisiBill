using System.Runtime.CompilerServices;

namespace DivisiBill.Services;

/// <summary>
/// Tracks availability of enum values using an array of boolean values.
/// </summary>
/// <remarks>Supports enums with up to 255 distinct values. All enum values are marked as available upon
/// initialization. This is an alternative to <see cref="EnumAvailability{TEnum}"/> that uses array storage
/// instead of a bitmap, allowing for more enum values at the cost of more memory.</remarks>
/// <typeparam name="TEnum">The enum type whose values are tracked. Must be a struct enum.</typeparam>
public sealed class EnumAvailabilityArray<TEnum> where TEnum : struct, Enum
{
    private readonly int _min;
    private readonly int _max;
    private readonly bool[] _availability;

    public EnumAvailabilityArray()
    {
        var values = Enum.GetValues<TEnum>().Select(e => Convert.ToInt32(e)).ToArray();
        _min = values.Min();
        _max = values.Max();

        int count = _max - _min + 1;
        if (count > 255)
            throw new InvalidOperationException("Enum too large, limit is 255");

        // All available initially
        _availability = new bool[count];
        Array.Fill(_availability, true);
    }

    private int Index(TEnum value)
    {
        int v = Convert.ToInt32(value);
        return v - _min;
    }

    // ⭐ The unified API
    public bool this[TEnum enumValue]
    {
        get
        {
            int index = Index(enumValue);
            return _availability[index];
        }
        set
        {
            int index = Index(enumValue);
            _availability[index] = value;
        }
    }

    public TEnum? GetLowestAvailable()
    {
        for (int i = 0; i < _availability.Length; i++)
        {
            if (_availability[i])
            {
                int enumValue = _min + i;
                return Unsafe.As<int, TEnum>(ref enumValue);
            }
        }
        return null;
    }

    public TEnum? GetHighestAvailable()
    {
        for (int i = _availability.Length - 1; i >= 0; i--)
        {
            if (_availability[i])
            {
                int enumValue = _min + i;
                return Unsafe.As<int, TEnum>(ref enumValue);
            }
        }
        return null;
    }
}
