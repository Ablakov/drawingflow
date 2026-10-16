using CommunityToolkit.Mvvm.ComponentModel;
using DrawingFlow.Client;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Organizations;
using DrawingFlow.Contracts.Projects;
using DrawingFlow.Desktop.Localization;

namespace DrawingFlow.Desktop.ViewModels.Dialogs;

/// <summary>Добавление участника проекта из числа участников организации или изменение его роли.</summary>
public sealed partial class ProjectMemberDialogViewModel : DialogViewModelBase
{
    private readonly DrawingFlowApiClient _api;
    private readonly Guid _projectId;
    private readonly ProjectMemberDto? _existing;

    public ProjectMemberDialogViewModel(DrawingFlowApiClient api, Guid projectId, ProjectMemberDto? existing, IReadOnlyList<OrganizationMemberDto> candidates)
    {
        _api = api;
        _projectId = projectId;
        _existing = existing;
        Candidates = candidates;
        Roles = RoleOptions.Project();
        _selectedCandidate = candidates.FirstOrDefault();
        _selectedRole = Roles.First(r => r.Value == (existing?.Role ?? ProjectRole.Engineer));
    }

    public override string Title => IsEdit ? "Роль участника проекта" : "Добавить участника проекта";

    public override string ConfirmText => IsEdit ? "Сохранить" : "Добавить";

    public bool IsEdit => _existing is not null;

    public bool IsCreate => !IsEdit;

    public string MemberDisplayName => _existing is null ? string.Empty : $"{_existing.DisplayName} ({_existing.Login})";

    public IReadOnlyList<OrganizationMemberDto> Candidates { get; }

    public IReadOnlyList<RoleOption<ProjectRole>> Roles { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private OrganizationMemberDto? _selectedCandidate;

    [ObservableProperty]
    private RoleOption<ProjectRole> _selectedRole;

    protected override bool CanConfirm() => base.CanConfirm() && (IsEdit || SelectedCandidate is not null);

    protected override Task<bool> OnConfirmAsync() => RunAsync(async () =>
    {
        if (IsEdit)
        {
            await _api.UpdateProjectMemberAsync(_projectId, _existing!.UserId, new UpdateProjectMemberRequest(SelectedRole.Value));
        }
        else
        {
            await _api.AddProjectMemberAsync(_projectId, new AddProjectMemberRequest(SelectedCandidate!.Login, SelectedRole.Value));
        }
    });
}
