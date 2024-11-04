using System.Globalization;

namespace DivisiBill.Services;

public class PercentConverter : IValueConverter
{

    public object Convert(object value, Type targetType, object parameter, CultureInfo cultureInfo)
    {
        double d;
        if (value.GetType() == typeof(int))
            d = (int)value / 100.0;
        else if (value.GetType() == typeof(double))
            d = (double)value;
        else
            return value;
        if (Math.Abs(d) > 1)
            return (d < 0) ? -100 : 100;

        if (targetType == typeof(string))
        {
            if (value.GetType() == typeof(int))
                return String.Format("{0:##0%}", d);
            else
                return String.Format("{0:##0.00#%}", d);
        }
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo cultureInfo)
    {
        string s = value.ToString().TrimEnd('%', ' ');
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
