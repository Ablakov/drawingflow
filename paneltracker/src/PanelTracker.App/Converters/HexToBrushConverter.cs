using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PanelTracker.App.Converters;

/// <summary>"#RRGGBB" → кисть; некорректное значение — серый.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static SolidColorBrush ToBrush(string? hex)
    {
        var key = string.IsNullOrWhiteSpace(hex) ? "#9E9E9E" : hex;
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            Color color;
            try
            {
                color = (Color)ColorConverter.ConvertFromString(key);
            }
            catch (FormatException)
            {
                color = Colors.Gray;
            }

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Cache[key] = brush;
            return brush;
        }
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => ToBrush(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
