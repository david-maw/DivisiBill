using System.Globalization;

namespace DivisiBill.Services;
public class BillCountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (int.TryParse(value?.ToString(), out int count))
        {
            return count switch
            {
                0 => "No Bills Selected",
                1 => "Selected 1 Bill Between",
                _ => $"Selected {count} Bills Between"
            };
        }
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}