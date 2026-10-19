using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PanelTracker.App.Converters;

/// <summary>Число больше нуля → Visible, иначе Collapsed.</summary>
public sealed class PositiveToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
