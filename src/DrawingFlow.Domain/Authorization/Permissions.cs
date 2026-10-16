namespace DrawingFlow.Domain.Authorization;

[Flags]
public enum OrganizationPermission
{
    None = 0,
    View = 1 << 0,
    ManageSettings = 1 << 1,
    ManageMembers = 1 << 2,
    CreateProjects = 1 << 3,
    ViewAllProjects = 1 << 4,
    ViewAudit = 1 << 5,
    ManageAgents = 1 << 6,
    CreateUsers = 1 << 7,
}

[Flags]
public enum ProjectPermission
{
    None = 0,
    View = 1 << 0,
    EditSettings = 1 << 1,
    ManageMembers = 1 << 2,
    RegisterDocuments = 1 << 3,
    UploadVersions = 1 << 4,
    SubmitForReview = 1 << 5,
    Review = 1 << 6,
    Release = 1 << 7,
    ViewAudit = 1 << 8,
    ManageWatchedFolders = 1 << 9,
}
