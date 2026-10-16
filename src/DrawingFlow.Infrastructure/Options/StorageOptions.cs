namespace DrawingFlow.Infrastructure.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Корневой каталог хранилища версий. Абсолютный путь или путь относительно каталога приложения. Не должен быть опубликован HTTP-сервером.</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>Максимальный размер одного файла в байтах (по умолчанию 1 ГиБ).</summary>
    public long MaxFileSizeBytes { get; set; } = 1024L * 1024 * 1024;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RootPath))
        {
            throw new InvalidOperationException(
                $"Параметр {SectionName}:RootPath не задан. Укажите каталог хранилища файлов (переменная окружения Storage__RootPath).");
        }

        if (MaxFileSizeBytes <= 0)
        {
            throw new InvalidOperationException($"Параметр {SectionName}:MaxFileSizeBytes должен быть положительным.");
        }
    }
}
