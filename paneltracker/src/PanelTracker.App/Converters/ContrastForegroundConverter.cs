using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PanelTracker.App.Converters;

/// <summary>Чёрный или белый текст в зависимости от яркости фона "#RRGGBB".</summary>
public sealed class ContrastForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = HexToBrushConverter.ToBrush(value as string).Color;
        var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
        return luminance > 0.6 ? Brushes.Black : Brushes.White;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
