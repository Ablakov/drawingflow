using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using DrawingFlow.Client;
using DrawingFlow.Contracts.Documents;
using DrawingFlow.Desktop.Localization;
using DrawingFlow.Desktop.Services;

namespace DrawingFlow.Desktop.ViewModels.Dialogs;

/// <summary>
/// Загрузка новой версии: локально вычисляется SHA-256, затем файл передаётся потоком с индикацией прогресса.
/// Ключ идемпотентности создаётся один раз на диалог, поэтому повтор после сбоя не создаёт дубликат.
/// </summary>
public sealed partial class UploadVersionDialogViewModel : DialogViewModelBase
{
    private readonly DrawingFlowApiClient _api;
    private readonly DocumentDto _document;
    private readonly string _filePath;
    private readonly string _idempotencyKey = Guid.NewGuid().ToString("N");

    public UploadVersionDialogViewModel(DrawingFlowApiClient api, DocumentDto document, string filePath)
    {
        _api = api;
        _document = document;
        _filePath = filePath;
        FileName = Path.GetFileName(filePath);
        var info = new FileInfo(filePath);
        FileSizeText = info.Exists ? Names.FormatSize(info.Length) : "файл не найден";
    }

    public override string Title => $"Новая версия документа {_document.DocumentNumber}";

    public override string ConfirmText => "Загрузить";

    public override double Width => 560;

    public string FileName { get; }

    public string FileSizeText { get; }

    public string NextVersionText => $"Будет зарегистрирована версия № {(_document.CurrentVersion?.VersionNumber ?? 0) + 1}";

    public DocumentVersionDto? Result { get; private set; }

    [ObservableProperty]
    private string _comment = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _progressText;

    protected override Task<bool> OnConfirmAsync() => RunAsync(async () =>
    {
        if (!File.Exists(_filePath))
        {
            throw new FileNotFoundException("Выбранный файл не найден. Возможно, он был перемещён или удалён.", _filePath);
        }

        ProgressText = "Вычисление контрольной суммы…";
        Progress = 0;
        var sha256 = await LocalFileCache.ComputeSha256Async(_filePath);

        await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var total = stream.Length;
        var progress = new Progress<long>(bytes =>
        {
            Progress = total > 0 ? Math.Min(100, bytes * 100.0 / total) : 100;
            ProgressText = $"Передано {Names.FormatSize(bytes)} из {Names.FormatSize(total)}";
        });

        Result = await _api.UploadDocumentVersionAsync(
            _document.Id,
            stream,
            FileName,
            contentType: null,
            comment: string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim(),
            idempotencyKey: _idempotencyKey,
            sha256Hash: sha256,
            progress: progress);
        ProgressText = $"Версия № {Result.VersionNumber} зарегистрирована.";
    });
}
