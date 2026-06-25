using System.Globalization;

namespace DivisiBill.Services;

public class PercentConverter : IValueConverter
{

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo cultureInfo)
    {
        double d;
        if (value is int intValue)
            d = intValue / 100.0;
        else if (value is double doubleValue)
            d = doubleValue;
        else
            return value;
        return Math.Abs(d) > 1
            ? (d < 0) ? -100 : 100
            : targetType == typeof(string)
            ? value is int ? string.Format("{0:##0%}", d) : string.Format("{0:##0.00#%}", d)
            : value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo cultureInfo)
    {
        string s = value?.ToString()?.TrimEnd('%', ' ') ?? string.Empty;
        if (targetType == typeof(double))
        {
            if (double.TryParse(s, out double d))
                return d / 100;
        }
        else if (targetType == typeof(int))
        {
            if (int.TryParse(s, out int i))
                return i;
        }
        return value;
    }
}
