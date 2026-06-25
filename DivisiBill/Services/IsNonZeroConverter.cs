using System.Globalization;

namespace DivisiBill.Services;

public class IsNonZeroConverter : IValueConverter
{

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo cultureInfo) => !IsZeroConverter.IsZero(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo cultureInfo) => throw new NotImplementedException();
}