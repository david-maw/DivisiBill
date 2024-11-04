using System.Globalization;

namespace DivisiBill.Services;

public class CurrencyConverter : IValueConverter
{

  public object Convert(object value, Type targetType, object parameter, CultureInfo cultureInfo)
  {
     if (targetType == typeof(string))
     {
        if ((parameter is null) || (parameter.GetType() != typeof(string)))
           return String.Format(CultureInfo.CurrentCulture, "{0:C}", value);
        else
           return String.Format(CultureInfo.CurrentCulture, (string)parameter, value);
     }
     return value;
  }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo cultureInfo) =>
          // The method converts only to decimal type.
          decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out decimal d) ? d : 0;
}


