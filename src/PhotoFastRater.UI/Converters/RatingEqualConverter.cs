using System.Globalization;
using System.Windows.Data;

namespace PhotoFastRater.UI.Converters;

public class RatingEqualConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int rating && int.TryParse(parameter?.ToString(), out var btn))
            return rating == btn;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
