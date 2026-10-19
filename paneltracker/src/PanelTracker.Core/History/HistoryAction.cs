namespace PanelTracker.Core.History;

/// <summary>Вид события в журнале истории.</summary>
public enum HistoryAction
{
    /// <summary>Перевод в другой статус из программы.</summary>
    Moved,

    /// <summary>Блокировка (HOLD).</summary>
    Held,

    /// <summary>Снятие блокировки.</summary>
    Released,

    /// <summary>Отметка «Проблема» с комментарием.</summary>
    ProblemMarked,

    /// <summary>Изменение статуса обнаружено при сканировании (файл перемещён вне программы).</summary>
    Detected,
}
