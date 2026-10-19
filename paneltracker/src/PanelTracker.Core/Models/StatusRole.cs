namespace PanelTracker.Core.Models;

/// <summary>Особая роль статуса: влияет на счётчики прогресса и на команды HOLD/«Проблема».</summary>
public enum StatusRole
{
    /// <summary>Обычный рабочий статус.</summary>
    Normal,

    /// <summary>Чертёж отправлен заказчику — учитывается в полосе прогресса.</summary>
    Sent,

    /// <summary>Панель с проблемой.</summary>
    Problem,

    /// <summary>Панель заблокирована (HOLD).</summary>
    Hold,
}
