using System.Globalization;

namespace DivisiBill.Services;

public class BoolToFontImageSourceConverter : IValueConverter
{
    public FontImageSource TrueValue { get; set; } = new()
    {
        Glyph = "\uF0130",
        FontFamily = "mdicons",
        Color = Colors.Green,
    };

    public FontImageSource FalseValue { get; set; } = new()
    {
        Glyph = "\uF0134",
        FontFamily = "mdicons",
        Color = Colors.Red,
    };

    public FontImageSource NullValue { get; set; } = new()
    {
        Glyph = "\uF0133",
        FontFamily = "mdicons",
        Color = Colors.Gray,
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            bool b when b => TrueValue,
            bool => FalseValue,
            null => NullValue,
            _ => NullValue,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FontImageSource fontImage)
            return null;

        if (ReferenceEquals(fontImage, TrueValue))
            return true;
        if (ReferenceEquals(fontImage, FalseValue))
            return false;
        return null;
    }
}
