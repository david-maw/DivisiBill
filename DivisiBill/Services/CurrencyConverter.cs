using System.Globalization;

namespace DivisiBill.Services;

public class CurrencyConverter : IValueConverter
{

    public object Convert(object value, Type targetType, object parameter, CultureInfo cultureInfo) => targetType == typeof(string)
            ? (parameter is null) || (parameter.GetType() != typeof(string))
                ? string.Format(CultureInfo.CurrentCulture, "{0:C}", value)
                : (object)string.Format(CultureInfo.CurrentCulture, (string)parameter, value)
            : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo cultureInfo) =>
          // The method converts only to decimal type.
          decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out decimal d) ? d : 0;
}


