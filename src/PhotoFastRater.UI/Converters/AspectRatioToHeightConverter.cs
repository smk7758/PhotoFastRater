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
        if (values.Length >= 2
            && values[0] is double ratio
            && values[1] is int size)
        {
            var clampedRatio = Math.Clamp(ratio, 0.5, 2.0);
            return (double)size * clampedRatio;
        }
        if (values.Length >= 2 && values[1] is int sz)
            return (double)sz;
        return 200.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
