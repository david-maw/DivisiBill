using System.Globalization;

namespace DivisiBill.Services;

public class IsZeroConverter : IValueConverter
{

    public static bool IsZero(object? value)
    {
        return (value is null)
            ? false
            : value switch
            {
                sbyte x => x == 0,
                byte x => x == 0,
                short x => x == 0,
                ushort x => x == 0,
                int x => x == 0,
                uint x => x == 0,
                long x => x == 0,
                ulong x => x == 0,
                float x => x == 0,
                double x => x == 0,
                decimal x => x == 0,
                _ => false,
            };
    }
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo cultureInfo) => IsZero(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo cultureInfo) => throw new NotImplementedException();
}