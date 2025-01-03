using System.Globalization;

namespace DivisiBill.Services;

internal class TrueToStringConverter : IValueConverter
{
    // Returns either the parameter or an empty string
    public object Convert(object value, Type targetType, object parameter, CultureInfo cultureInfo) => value is bool && parameter is not null && parameter is string
            ? (object)((bool)value ? "" : (string)parameter)
            : throw new ArgumentException();
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo cultureInfo) => throw new NotImplementedException();
}
