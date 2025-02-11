using System.Globalization;

namespace DivisiBill.Services;

internal class TrueToStringConverter : IValueConverter
{
    // Returns either the parameter or an empty string
    public object Convert(object value, Type targetType, object parameter, CultureInfo cultureInfo) =>
        value is not bool boolValue
            ? throw new ArgumentException("Not a boolean value", nameof(value))
            : parameter is not string and not null
            ? throw new ArgumentException("Not a string", nameof(parameter))
            : (object)(boolValue ? "" : (string)parameter);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo cultureInfo) => throw new NotImplementedException();
}
