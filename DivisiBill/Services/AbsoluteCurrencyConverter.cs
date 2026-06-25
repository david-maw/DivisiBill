using System.Globalization;

namespace DivisiBill.Services;

public sealed class AbsoluteCurrencyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
            return string.Empty;

        // Try to parse as decimal/double
        if (decimal.TryParse(value.ToString(), out decimal amount))
        {
            // Apply Math.Abs and format as currency
            return Math.Abs(amount).ToString("C", culture);
        }

        return value.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("ConvertBack is not supported.");
}