using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrawingFlow.Client;
using DrawingFlow.Contracts.Agents;
using DrawingFlow.Contracts.Documents;
using DrawingFlow.Contracts.Projects;
using DrawingFlow.Desktop.Localization;

namespace DrawingFlow.Desktop.ViewModels.Dialogs;

public sealed partial class RegisterAgentDialogViewModel(DrawingFlowApiClient api, Guid organizationId) : DialogViewModelBase
{
    public override string Title => "Регистрация агента";

    public override string ConfirmText => "Зарегистрировать";

    public AgentRegistrationResultDto? Result { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _name = string.Empty;

    protected override bool CanConfirm() => base.CanConfirm() && !string.IsNullOrWhiteSpace(Name);

    protected override Task<bool> OnConfirmAsync() => RunAsync(async () =>
    {
        Result = await api.RegisterAgentAsync(organizationId, new RegisterAgentRequest(Name.Trim()));
    });
}

/// <summary>Показывает ключ агента один раз после регистрации.</summary>
public sealed partial class AgentKeyDialogViewModel(AgentRegistrationResultDto registration) : DialogViewModelBase
{
    public override string Title => "Ключ агента";

    public override string ConfirmText => "Готово";

    public override bool ShowCancel => false;

    public override double Width => 620;

    public string AgentName => registration.Agent.Name;

    public string AgentKey => registration.AgentKey;

    public string RegisterCommandLine => $"DrawingFlow.Agent register --server <адрес сервера> --key {registration.AgentKey}";

    [ObservableProperty]
    private string? _copyStatus;

    [RelayCommand]
    private void CopyKey()
    {
        Clipboard.SetText(AgentKey);
        CopyStatus = "Ключ скопирован в буфер обмена.";
    }

    [RelayCommand]
    private void CopyCommand()
    {
        Clipboard.SetText(RegisterCommandLine);
        CopyStatus = "Команда скопирована в буфер обмена.";
    }

    protected override Task<bool> OnConfirmAsync() => Task.FromResult(true);
}

public sealed partial class WatchedFolderDialogViewModel : DialogViewModelBase
{
    private readonly DrawingFlowApiClient _api;
    private readonly Guid _agentId;
    private readonly WatchedFolderDto? _existing;

    public WatchedFolderDialogViewModel(DrawingFlowApiClient api, Guid agentId, IReadOnlyList<ProjectDto> projects, WatchedFolderDto? existing)
    {
        _api = api;
        _agentId = agentId;
        _existing = existing;
        Projects = projects;
        Rules = Enum.GetValues<FileMatchingRule>().Select(r => new RoleOption<FileMatchingRule>(r, Names.Of(r))).ToList();
        _selectedProject = projects.FirstOrDefault(p => p.Id == existing?.ProjectId) ?? projects.FirstOrDefault();
        _path = existing?.Path ?? string.Empty;
        _includePatterns = existing?.IncludePatterns ?? "*.dwg;*.dxf;*.pdf;*.rvt;*.ifc";
        _excludePatterns = existing?.ExcludePatterns ?? string.Empty;
        _selectedRule = Rules.First(r => r.Value == (existing?.MatchingRule ?? FileMatchingRule.NumberFromFileName));
        _documentNumberPattern = existing?.DocumentNumberPattern ?? string.Empty;
        _autoRegisterUnknown = existing?.AutoRegisterUnknown ?? false;
        _isEnabled = existing?.IsEnabled ?? true;
    }

    public override string Title => IsEdit ? "Изменение папки наблюдения" : "Новая папка наблюдения";

    public override string ConfirmText => IsEdit ? "Сохранить" : "Добавить";

    public override double Width => 600;

    public bool IsEdit => _existing is not null;

    public bool IsCreate => !IsEdit;

    public IReadOnlyList<ProjectDto> Projects { get; }

    public IReadOnlyList<RoleOption<FileMatchingRule>> Rules { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private ProjectDto? _selectedProject;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _path;

    [ObservableProperty]
    private string _includePatterns;

    [ObservableProperty]
    private string _excludePatterns;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesNumberPattern))]
    private RoleOption<FileMatchingRule> _selectedRule;

    public bool UsesNumberPattern => SelectedRule.Value == FileMatchingRule.NumberFromFileName;

    [ObservableProperty]
    private string _documentNumberPattern;

    [ObservableProperty]
    private bool _autoRegisterUnknown;

    [ObservableProperty]
    private bool _isEnabled;

    protected override bool CanConfirm() => base.CanConfirm() && SelectedProject is not null && !string.IsNullOrWhiteSpace(Path);

    protected override Task<bool> OnConfirmAsync() => RunAsync(async () =>
    {
        static string? Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var request = new SaveWatchedFolderRequest(
            SelectedProject!.Id,
            Path.Trim(),
            Clean(IncludePatterns) ?? "*",
            Clean(ExcludePatterns),
            SelectedRule.Value,
            UsesNumberPattern ? Clean(DocumentNumberPattern) : null,
            AutoRegisterUnknown,
            IsEnabled);
        if (IsEdit)
        {
            await _api.UpdateWatchedFolderAsync(_existing!.Id, request);
        }
        else
        {
            await _api.CreateWatchedFolderAsync(_agentId, request);
        }
    });
}

/// <summary>Ручное сопоставление файла с документом: кандидаты агента или поиск по реестру проекта.</summary>
public sealed partial class ResolveOperationDialogViewModel : DialogViewModelBase
{
    private readonly DrawingFlowApiClient _api;
    private readonly SyncOperationDto _operation;

    public ResolveOperationDialogViewModel(DrawingFlowApiClient api, SyncOperationDto operation)
    {
        _api = api;
        _operation = operation;
        foreach (var candidate in operation.Candidates)
        {
            Candidates.Add(new DocumentChoice(candidate.DocumentId, candidate.DocumentNumber, candidate.Title));
        }

        _selectedDocument = Candidates.FirstOrDefault();
        _searchText = string.Empty;
    }

    public sealed record DocumentChoice(Guid Id, string DocumentNumber, string Title)
    {
        public override string ToString() => $"{DocumentNumber} — {Title}";
    }

    public override string Title => "Сопоставление файла с документом";

    public override string ConfirmText => "Загрузить в документ";

    public override double Width => 640;

    public string FileName => _operation.FileName;

    public string? Error => _operation.LastError;

    public ObservableCollection<DocumentChoice> Candidates { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private DocumentChoice? _selectedDocument;

    [ObservableProperty]
    private string _searchText;

    protected override bool CanConfirm() => base.CanConfirm() && SelectedDocument is not null;

    [RelayCommand]
    private Task SearchAsync() => RunAsync(async () =>
    {
        var documents = await _api.GetDocumentsAsync(_operation.ProjectId, new DocumentListQuery(Search: string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(), PageSize: 50));
        Candidates.Clear();
        foreach (var document in documents.Items.Where(d => d.Status != DocumentStatus.Archived))
        {
            Candidates.Add(new DocumentChoice(document.Id, document.DocumentNumber, document.Title));
        }

        SelectedDocument = Candidates.FirstOrDefault();
    });

    protected override Task<bool> OnConfirmAsync() => RunAsync(() => _api.ResolveSyncOperationAsync(_operation.Id, new ResolveSyncOperationRequest(SelectedDocument!.Id)));
}
