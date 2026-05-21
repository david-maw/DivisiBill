using System.Numerics;
using System.Runtime.CompilerServices;

namespace DivisiBill.Services;

/// <summary>
/// Tracks availability of enum values using a 31-bit mask.
/// </summary>
/// <remarks>Supports enums with up to 31 distinct values. All enum values are marked as available upon
/// initialization. Unfortunately this fails mysteriously in Android phones and worse - it seems to cause a
/// Visual Studio hang when it does, so for now it is switched for  <see cref="EnumAvailabilityArray{TEnum}"/>
/// a simpler, but slower version using a bool array for storage.</remarks>
/// <typeparam name="TEnum">The enum type whose values are tracked, whose values must span a range of 31 or less.
/// In practice this means an enumValue type enum as opposed to one used for bit flags.</typeparam>
public sealed class EnumAvailability<TEnum> where TEnum : struct, Enum
{
    private readonly int _min;
    private readonly int _max;
    private int _mask;

    public EnumAvailability()
    {
        var values = Enum.GetValues<TEnum>().Select(e => Convert.ToInt32(e)).ToArray();
        _min = values.Min();
        _max = values.Max();

        int count = _max - _min + 1;
        if (count > 31)
            throw new InvalidOperationException("Enum too large, limit is 31");

        // All available initially
        _mask = (1 << count) - 1;
    }

    private int Bit(TEnum value)
    {
        int v = Convert.ToInt32(value);
        return v - _min;
    }

    // ⭐ The unified API
    public bool this[TEnum enumValue]
    {
        get
        {
            int bit = Bit(enumValue);
            return (_mask & (1 << bit)) != 0;
        }
        set
        {
            int bit = Bit(enumValue);
            if (value)
                _mask |= 1 << bit;      // mark available
            else
                _mask &= ~(1 << bit);   // mark in-use
        }
    }
    public TEnum? GetLowestAvailable()
    {
        if (_mask == 0)
            return null;

        int lowestBit = _mask & -_mask; // isolate lowest set bit
        int index = (int)Math.Log2(lowestBit);

        int enumValue = _min + index;
        return Unsafe.As<int, TEnum>(ref enumValue);
    }
    public TEnum? GetHighestAvailable()
    {
        if (_mask == 0)
            return null;

        int index = BitOperations.Log2((uint)_mask);   // highest set bit
        int enumValue = _min + index;

        return Unsafe.As<int, TEnum>(ref enumValue);
    }
}
