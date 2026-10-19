using System.Globalization;
using System.Windows.Data;
using PanelTracker.Core.Models;

namespace PanelTracker.App.Converters;

/// <summary>Роль статуса → название для интерфейса.</summary>
public sealed class RoleNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        StatusRole.Normal => "обычный",
        StatusRole.Sent => "отправлено (прогресс)",
        StatusRole.Problem => "проблема",
        StatusRole.Hold => "HOLD",
        _ => value?.ToString() ?? string.Empty,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
