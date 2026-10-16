using System.Windows;

namespace DrawingFlow.Desktop.Behaviors;

/// <summary>Переносит DataContext в элементы вне визуального дерева (например, в столбцы DataGrid).</summary>
public sealed class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
