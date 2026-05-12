using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PhotoFastRater.UI.Converters;

/// <summary>
/// null または空文字列の場合 Collapsed、それ以外は Visible を返すコンバーター。
/// nullable な値型（int?, double? など）にも対応。
/// </summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public class NullOrEmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return Visibility.Collapsed;
        if (value is string s && string.IsNullOrWhiteSpace(s)) return Visibility.Collapsed;
        return Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
