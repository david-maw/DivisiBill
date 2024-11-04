using System.Globalization;

namespace DivisiBill.Services;

internal class DistanceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || value.GetType() != typeof(int) || (int)value >= Distances.Inaccurate)
            return null;
        return Distances.Text((int)value);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
