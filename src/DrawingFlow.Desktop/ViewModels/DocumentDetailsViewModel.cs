using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrawingFlow.Client;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Documents;
using DrawingFlow.Contracts.Workflow;
using DrawingFlow.Desktop.Localization;
using DrawingFlow.Desktop.Services;
using DrawingFlow.Desktop.ViewModels.Dialogs;
using Microsoft.Extensions.Logging;

namespace DrawingFlow.Desktop.ViewModels;

/// <summary>Карточка документа: метаданные, история версий, загрузка новой версии, скачивание и открытие файлов.</summary>
public sealed partial class DocumentDetailsViewModel(
    DrawingFlowApiClient api,
    ShellViewModel shell,
    IDialogService dialogs,
    IFileLauncher launcher,
    LocalFileCache cache,
    ConnectionMonitor connection,
    ILogger<DocumentDetailsViewModel> logger) : PageViewModelBase
{
    public override string Title => Document?.DocumentNumber ?? "Документ";

    public Guid DocumentId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(StatusName))]
    [NotifyPropertyChangedFor(nameof(IsArchived))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanUpload))]
    [NotifyPropertyChangedFor(nameof(CanArchive))]
    [NotifyPropertyChangedFor(nameof(ArchiveActionText))]
    private DocumentDto? _document;

    public ObservableCollection<DocumentVersionDto> Versions { get; } = [];

    public ObservableCollection<ReviewDto> Reviews { get; } = [];

    public ObservableCollection<WorkflowTaskDto> OpenTasks { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyPropertyChangedFor(nameof(CanDecide))]
    [NotifyPropertyChangedFor(nameof(CanRelease))]
    [NotifyPropertyChangedFor(nameof(HasWorkflowActions))]
    private DocumentWorkflowDto? _workflow;

    public bool CanSubmit => Workflow?.Can(WorkflowActions.Submit) == true;

    public bool CanDecide => Workflow?.Can(WorkflowActions.Approve) == true;

    public bool CanRelease => Workflow?.Can(WorkflowActions.Release) == true;

    public bool HasWorkflowActions => CanSubmit || CanDecide || CanRelease;

    [ObservableProperty]
    private DocumentVersionDto? _selectedVersion;

    [ObservableProperty]
    private bool _versionsLoaded;

    [ObservableProperty]
    private bool _isTransferring;

    [ObservableProperty]
    private double _transferProgress;

    [ObservableProperty]
    private string? _transferText;

    public string StatusName => Document is null ? string.Empty : Names.Of(Document.Status);

    public bool IsArchived => Document?.Status == DocumentStatus.Archived;

    public bool CanEdit => Document?.Can(ProjectPermissions.RegisterDocuments) == true && !IsArchived;

    public bool CanUpload => Document?.Can(ProjectPermissions.UploadVersions) == true && !IsArchived;

    public bool CanArchive => Document?.Can(ProjectPermissions.EditSettings) == true;

    public string ArchiveActionText => IsArchived ? "Восстановить из архива" : "В архив";

    public override async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await RunAsync(async () =>
        {
            var workflowTask = api.GetDocumentWorkflowAsync(DocumentId, cancellationToken);
            var versionsTask = api.GetDocumentVersionsAsync(DocumentId, cancellationToken);
            var workflow = await workflowTask;
            Workflow = workflow;
            Document = workflow.Document;
            Reviews.Clear();
            foreach (var review in workflow.Reviews)
            {
                Reviews.Add(review);
            }

            OpenTasks.Clear();
            foreach (var task in workflow.Tasks.Where(t => t.Status == WorkflowTaskStatus.Open))
            {
                OpenTasks.Add(task);
            }

            Versions.Clear();
            foreach (var version in await versionsTask)
            {
                Versions.Add(version);
            }

            VersionsLoaded = true;
        }, connection, logger);
    }

    [RelayCommand]
    private void Back()
    {
        if (Document is not null)
        {
            shell.ShowDocuments(Document.ProjectId);
        }
        else
        {
            shell.NavigateTo(ShellViewModel.DocumentsKey);
        }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (Document is null || !CanEdit)
        {
            return;
        }

        var dialog = new DocumentEditorDialogViewModel(api, Document.ProjectId, Document);
        if (dialogs.ShowDialog(dialog))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task ToggleArchiveAsync()
    {
        if (Document is null || !CanArchive)
        {
            return;
        }

        var question = IsArchived
            ? $"Восстановить документ «{Document.DocumentNumber}» из архива?"
            : $"Отправить документ «{Document.DocumentNumber}» в архив? Загрузка новых версий станет невозможной до восстановления.";
        if (!dialogs.Confirm(question, "Архив документов"))
        {
            return;
        }

        var ok = await RunAsync(async () =>
        {
            Document = IsArchived ? await api.RestoreDocumentAsync(Document.Id) : await api.ArchiveDocumentAsync(Document.Id);
        }, connection, logger);
        if (ok)
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task SubmitForReviewAsync()
    {
        if (Document?.CurrentVersion is null || !CanSubmit)
        {
            return;
        }

        IReadOnlyList<ReviewerCandidateDto> candidates = [];
        if (!await RunAsync(async () => candidates = await api.GetReviewerCandidatesAsync(Document.ProjectId), connection, logger))
        {
            return;
        }

        if (candidates.Count == 0)
        {
            dialogs.ShowInfo("В проекте нет пользователей с ролью «Проверяющий» или «Руководитель проекта». Назначьте проверяющих в составе проекта.", "Согласование");
            return;
        }

        var dialog = new SubmitForReviewDialogViewModel(api, Document, candidates);
        if (dialogs.ShowDialog(dialog))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private Task ApproveAsync() => DecideAsync(ReviewDecision.Approved);

    [RelayCommand]
    private Task RequestRevisionAsync() => DecideAsync(ReviewDecision.RevisionRequired);

    private async Task DecideAsync(ReviewDecision decision)
    {
        if (Document?.CurrentVersion is null || !CanDecide)
        {
            return;
        }

        var dialog = new ReviewDecisionDialogViewModel(api, Document.Id, Document.CurrentVersion.Id, $"{Document.DocumentNumber} — {Document.Title}, версия {Document.CurrentVersion.VersionNumber}", decision);
        if (dialogs.ShowDialog(dialog))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task ReleaseAsync()
    {
        if (Document?.CurrentVersion is null || !CanRelease)
        {
            return;
        }

        if (!dialogs.Confirm($"Выпустить документ «{Document.DocumentNumber}» (версия {Document.CurrentVersion.VersionNumber})? Выпущенная версия останется в истории и не может быть заменена.", "Выпуск документа"))
        {
            return;
        }

        if (await RunAsync(() => api.ReleaseDocumentAsync(Document.Id, new ReleaseRequest(Document.CurrentVersion.Id)), connection, logger))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task UploadVersionAsync()
    {
        if (Document is null || !CanUpload)
        {
            return;
        }

        var path = dialogs.OpenFile("Выберите файл новой версии документа");
        if (path is null)
        {
            return;
        }

        var dialog = new UploadVersionDialogViewModel(api, Document, path);
        if (dialogs.ShowDialog(dialog))
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task DownloadAsync(DocumentVersionDto? version)
    {
        if (Document is null || version is null)
        {
            return;
        }

        var path = dialogs.SaveFile("Сохранить версию документа", version.OriginalFileName);
        if (path is null)
        {
            return;
        }

        if (await DownloadToAsync(version, path))
        {
            dialogs.ShowInfo($"Версия {version.VersionNumber} сохранена:\n{path}", "Скачивание завершено");
        }
    }

    [RelayCommand]
    private async Task OpenAsync(DocumentVersionDto? version)
    {
        var path = await EnsureCachedAsync(version);
        if (path is not null)
        {
            await RunAsync(() =>
            {
                launcher.Open(path);
                return Task.CompletedTask;
            }, logger: logger);
        }
    }

    [RelayCommand]
    private async Task RevealInFolderAsync(DocumentVersionDto? version)
    {
        var path = await EnsureCachedAsync(version);
        if (path is not null)
        {
            await RunAsync(() =>
            {
                launcher.RevealInFolder(path);
                return Task.CompletedTask;
            }, logger: logger);
        }
    }

    [RelayCommand]
    private void CompareWithCurrent(DocumentVersionDto? version)
    {
        if (Document?.CurrentVersion is null || version is null)
        {
            return;
        }

        if (version.Id == Document.CurrentVersion.Id)
        {
            dialogs.ShowInfo("Это текущая версия документа. Выберите другую версию для сравнения.", "Сравнение версий");
            return;
        }

        dialogs.ShowDialog(new CompareVersionsDialogViewModel(Document.CurrentVersion, version));
    }

    /// <summary>Гарантирует наличие проверенной по SHA-256 локальной копии версии; возвращает путь или null при ошибке.</summary>
    private async Task<string?> EnsureCachedAsync(DocumentVersionDto? version)
    {
        if (Document is null || version is null)
        {
            return null;
        }

        var path = cache.GetPath(version);
        if (await cache.IsValidAsync(path, version.Sha256Hash))
        {
            return path;
        }

        return await DownloadToAsync(version, path) ? path : null;
    }

    private async Task<bool> DownloadToAsync(DocumentVersionDto version, string destinationPath)
    {
        IsTransferring = true;
        TransferProgress = 0;
        TransferText = $"Скачивание версии {version.VersionNumber}…";
        var tempPath = destinationPath + ".part";
        try
        {
            return await RunAsync(async () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                var progress = new Progress<long>(bytes =>
                {
                    TransferProgress = version.FileSize > 0 ? Math.Min(100, bytes * 100.0 / version.FileSize) : 100;
                    TransferText = $"Скачивание версии {version.VersionNumber}: {Names.FormatSize(bytes)} из {Names.FormatSize(version.FileSize)}";
                });

                await using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
                {
                    await api.DownloadDocumentVersionAsync(version.DocumentId, version.Id, file, progress);
                }

                var actualHash = await LocalFileCache.ComputeSha256Async(tempPath);
                if (!string.Equals(actualHash, version.Sha256Hash, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(tempPath);
                    throw new InvalidDataException("Контрольная сумма скачанного файла не совпадает с зарегистрированной. Файл удалён; повторите скачивание.");
                }

                File.Move(tempPath, destinationPath, overwrite: true);
            }, connection, logger);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // Временный файл; удалится при следующей попытке.
                }
            }

            IsTransferring = false;
            TransferText = null;
        }
    }
}
