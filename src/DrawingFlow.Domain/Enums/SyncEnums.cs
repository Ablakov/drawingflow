namespace DrawingFlow.Domain.Enums;

/// <summary>Правило сопоставления файла в папке наблюдения с документом проекта.</summary>
public enum FileMatchingRule
{
    /// <summary>Номер документа извлекается из имени файла регулярным выражением (группа <c>number</c>).</summary>
    NumberFromFileName = 0,
    /// <summary>Имя файла без расширения совпадает с номером документа.</summary>
    FileNameIsNumber = 1,
    /// <summary>Имя файла совпадает с именем файла текущей версии документа.</summary>
    VersionFileName = 2,
}

/// <summary>Состояние операции синхронизации (единая модель для агента и сервера).</summary>
public enum SyncOperationStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    NeedsResolution = 4,
    /// <summary>Файл исчез до загрузки; данные на сервере не затронуты.</summary>
    Cancelled = 5,
    /// <summary>Пользователь отклонил операцию.</summary>
    Dismissed = 6,
}

/// <summary>Действие, запрошенное пользователем у агента.</summary>
public enum SyncRequestedAction
{
    None = 0,
    Retry = 1,
    UploadToDocument = 2,
    Dismiss = 3,
}
