using CommunityToolkit.Mvvm.ComponentModel;
using DrawingFlow.Client;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Projects;
using DrawingFlow.Desktop.Localization;

namespace DrawingFlow.Desktop.ViewModels.Dialogs;

public sealed partial class ProjectEditorDialogViewModel : DialogViewModelBase
{
    private readonly DrawingFlowApiClient _api;
    private readonly Guid _organizationId;
    private readonly ProjectDto? _existing;

    public ProjectEditorDialogViewModel(DrawingFlowApiClient api, Guid organizationId, ProjectDto? existing)
    {
        _api = api;
        _organizationId = organizationId;
        _existing = existing;
        _code = existing?.Code ?? string.Empty;
        _name = existing?.Name ?? string.Empty;
        _description = existing?.Description ?? string.Empty;
        _selectedStatus = StatusOptions.First(s => s.Value == (existing?.Status ?? ProjectStatus.Active));
    }

    public override string Title => IsEdit ? "Изменение проекта" : "Новый проект";

    public override string ConfirmText => IsEdit ? "Сохранить" : "Создать";

    public bool IsEdit => _existing is not null;

    public bool IsCreate => !IsEdit;

    public ProjectDto? Result { get; private set; }

    public IReadOnlyList<RoleOption<ProjectStatus>> StatusOptions { get; } = RoleOptions.ProjectStatuses();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _code;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _name;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private RoleOption<ProjectStatus> _selectedStatus;

    protected override bool CanConfirm() => base.CanConfirm() && !string.IsNullOrWhiteSpace(Code) && !string.IsNullOrWhiteSpace(Name);

    protected override async Task<bool> OnConfirmAsync()
    {
        var description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        return await RunAsync(async () =>
        {
            Result = IsEdit
                ? await _api.UpdateProjectAsync(_existing!.Id, new UpdateProjectRequest(Name.Trim(), description, SelectedStatus.Value))
                : await _api.CreateProjectAsync(_organizationId, new CreateProjectRequest(Code.Trim(), Name.Trim(), description));
        });
    }
}
