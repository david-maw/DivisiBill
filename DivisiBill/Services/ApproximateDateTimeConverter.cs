using System.Globalization;

namespace DivisiBill.Services;

internal class ApproximateDateTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is DateTime dateTime && targetType == typeof(string) ? (object)(dateTime.ApproximateDateTime()) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}