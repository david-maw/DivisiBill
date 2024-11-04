using System.Globalization;

namespace DivisiBill.Services;

public class IsNonZeroConverter : IValueConverter
{

    public object Convert(object value, Type targetType, object parameter, CultureInfo cultureInfo)
    {
        if (value is null)
            return false;

        switch (value)
        {
            case sbyte x: return x != 0;
            case byte x: return x != 0;
            case short x: return x != 0;
            case ushort x: return x != 0;
            case int x: return x != 0;
            case uint x: return x != 0;
            case long x: return x != 0;
            case ulong x: return x != 0;
            case float x: return x != 0;
            case double x: return x != 0;
            case decimal x: return x != 0;
            default: return false;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo cultureInfo)
    {
        throw new NotImplementedException();
    }
}