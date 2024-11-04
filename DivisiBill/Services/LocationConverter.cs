using System.Globalization;

namespace DivisiBill.Services;

internal class LocationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value is null) ? null : value.GetType() != typeof(Location) ? "*TypeError*" :
        Utilities.MakeLocationText((Location)value);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
