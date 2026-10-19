using System.IO;
using System.Windows;
using Microsoft.Win32;
using PanelTracker.App.Views;

namespace PanelTracker.App.Services;

public sealed class WpfUserInteraction : IUserInteraction
{
    private static Window? Owner => Application.Current?.MainWindow is { IsLoaded: true } w ? w : null;

    public string? PickFolder(string? initialFolder)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите корневую папку проекта",
            Multiselect = false,
        };
        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        var accepted = Owner is null ? dialog.ShowDialog() : dialog.ShowDialog(Owner);
        return accepted == true ? dialog.FolderName : null;
    }

    public string? Prompt(string title, string label, string? initialValue = null)
    {
        var window = new TextPromptWindow(title, label, initialValue) { Owner = Owner };
        return window.ShowDialog() == true ? window.Value : null;
    }

    public bool Confirm(string message) =>
        MessageBox.Show(message, "Panel Tracker", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowError(string message) =>
        MessageBox.Show(message, "Panel Tracker — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string message) =>
        MessageBox.Show(message, "Panel Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
}
