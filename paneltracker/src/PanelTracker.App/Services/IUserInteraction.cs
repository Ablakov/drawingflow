namespace PanelTracker.App.Services;

/// <summary>Диалоги с пользователем, отделённые от модели представления.</summary>
public interface IUserInteraction
{
    string? PickFolder(string? initialFolder);

    /// <summary>Запрос текста; null — пользователь отменил.</summary>
    string? Prompt(string title, string label, string? initialValue = null);

    bool Confirm(string message);

    void ShowError(string message);

    void ShowInfo(string message);
}
