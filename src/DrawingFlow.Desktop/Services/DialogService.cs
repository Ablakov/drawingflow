using System.Windows;
using DrawingFlow.Desktop.ViewModels.Dialogs;
using DrawingFlow.Desktop.Views.Dialogs;

namespace DrawingFlow.Desktop.Services;

public interface IDialogService
{
    /// <summary>Показывает модальный диалог; true — подтверждён.</summary>
    bool ShowDialog(DialogViewModelBase viewModel);

    void ShowError(string message, string title = "Ошибка");

    void ShowInfo(string message, string title = "DrawingFlow");

    bool Confirm(string message, string title = "Подтверждение");

    /// <summary>Диалог выбора файла; null — отмена.</summary>
    string? OpenFile(string title, string filter = "Все файлы (*.*)|*.*");

    /// <summary>Диалог сохранения файла; null — отмена.</summary>
    string? SaveFile(string title, string suggestedFileName, string filter = "Все файлы (*.*)|*.*");
}

public sealed class DialogService : IDialogService
{
    private static Window? ActiveWindow =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current.MainWindow;

    public bool ShowDialog(DialogViewModelBase viewModel)
    {
        var window = new DialogWindow { DataContext = viewModel, Owner = ActiveWindow };
        return window.ShowDialog() == true;
    }

    public void ShowError(string message, string title = "Ошибка") =>
        MessageBox.Show(ActiveWindow ?? new Window(), message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string message, string title = "DrawingFlow") =>
        MessageBox.Show(ActiveWindow ?? new Window(), message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool Confirm(string message, string title = "Подтверждение") =>
        MessageBox.Show(ActiveWindow ?? new Window(), message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? OpenFile(string title, string filter = "Все файлы (*.*)|*.*")
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true, Multiselect = false };
        return dialog.ShowDialog(ActiveWindow) == true ? dialog.FileName : null;
    }

    public string? SaveFile(string title, string suggestedFileName, string filter = "Все файлы (*.*)|*.*")
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = title, Filter = filter, FileName = suggestedFileName, OverwritePrompt = true };
        return dialog.ShowDialog(ActiveWindow) == true ? dialog.FileName : null;
    }
}
