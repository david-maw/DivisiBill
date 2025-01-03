using System.Globalization;

namespace DivisiBill.Services;

internal class DistanceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is null || value.GetType() != typeof(int) || (int)value >= Distances.Inaccurate ? null : (object)Distances.Text((int)value);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
