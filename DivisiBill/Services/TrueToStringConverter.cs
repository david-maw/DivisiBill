using System.Globalization;

namespace DivisiBill.Services;

internal class TrueToStringConverter : IValueConverter
{
    // Returns either the parameter or an empty string
    public object Convert(object value, Type targetType, object parameter, CultureInfo cultureInfo)
    {
        if (value is bool && parameter is not null && parameter is string)
            return (bool)value ? "" : (string)parameter;
        else
            throw new ArgumentException();
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo cultureInfo) => throw new NotImplementedException();
}
