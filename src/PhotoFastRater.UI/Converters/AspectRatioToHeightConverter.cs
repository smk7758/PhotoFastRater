using System.Globalization;
using System.Windows.Data;

namespace PhotoFastRater.UI.Converters;

/// <summary>
/// [PhotoAspectRatio, ThumbnailSize] → Height (= ThumbnailSize * AspectRatio, 0.5〜2.0 に制限)
/// </summary>
public class AspectRatioToHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        bool uniform = values.Length >= 3 && values[2] is bool u && u;
        if (values.Length >= 2 && values[1] is int size)
        {
            if (uniform) return (double)size;
            if (values[0] is double ratio)
                return (double)size * Math.Clamp(ratio, 0.5, 2.0);
            return (double)size;
        }
        return 200.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
