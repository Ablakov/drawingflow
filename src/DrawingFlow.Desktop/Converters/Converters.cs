using System.Globalization;
using System.Windows;
using System.Windows.Data;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Agents;
using DrawingFlow.Contracts.Documents;
using DrawingFlow.Contracts.Workflow;
using DrawingFlow.Desktop.Localization;

namespace DrawingFlow.Desktop.Converters;

/// <summary>Роль или статус → русское название.</summary>
public sealed class DisplayNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        OrganizationRole role => Names.Of(role),
        ProjectRole role => Names.Of(role),
        ProjectStatus status => Names.Of(status),
        DocumentStatus status => Names.Of(status),
        VersionStatus status => Names.Of(status),
        SyncOperationStatus status => Names.Of(status),
        FileMatchingRule rule => Names.Of(rule),
        SyncRequestedAction action => Names.Of(action),
        ReviewDecision decision => Names.Of(decision),
        WorkflowTaskType taskType => Names.Of(taskType),
        WorkflowTaskStatus taskStatus => Names.Of(taskStatus),
        bool active => active ? "Активен" : "Заблокирован",
        null => string.Empty,
        _ => value.ToString(),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class AuditActionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string action ? Names.OfAuditAction(action) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class EntityTypeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string type ? Names.OfEntityType(type) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>UTC → местное время в формате «дд.ММ.гггг ЧЧ:мм».</summary>
public sealed class LocalDateTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DateTime utc => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString(parameter as string ?? "dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("ru-RU")),
        null => string.Empty,
        _ => value.ToString(),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is false;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is false;
}

/// <summary>true → Collapsed, false → Visible (обратный BooleanToVisibilityConverter).</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Непустая строка или ненулевой объект → Visible.</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        null => Visibility.Collapsed,
        string s when string.IsNullOrWhiteSpace(s) => Visibility.Collapsed,
        _ => Visibility.Visible,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Число 0 → Visible (для подсказки «список пуст»), иначе Collapsed.</summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        int count => count == 0 ? Visibility.Visible : Visibility.Collapsed,
        _ => Visibility.Collapsed,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Размер в байтах → читаемая строка.</summary>
public sealed class FileSizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        long bytes => Names.FormatSize(bytes),
        int bytes => Names.FormatSize(bytes),
        _ => string.Empty,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Сокращённое отображение SHA-256 (первые 12 символов).</summary>
public sealed class ShortHashConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string hash && hash.Length > 12 ? hash[..12] + "…" : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
