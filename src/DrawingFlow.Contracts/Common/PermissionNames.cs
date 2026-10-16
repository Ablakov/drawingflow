namespace DrawingFlow.Contracts;

// Имена прав совпадают с членами перечислений DrawingFlow.Domain.Authorization (проверяется тестом).
// Сервер вычисляет и возвращает действующие права; клиент только отражает их в интерфейсе.

public static class OrganizationPermissions
{
    public const string View = "View";
    public const string ManageSettings = "ManageSettings";
    public const string ManageMembers = "ManageMembers";
    public const string CreateProjects = "CreateProjects";
    public const string ViewAllProjects = "ViewAllProjects";
    public const string ViewAudit = "ViewAudit";
    public const string ManageAgents = "ManageAgents";
    public const string CreateUsers = "CreateUsers";
}

public static class ProjectPermissions
{
    public const string View = "View";
    public const string EditSettings = "EditSettings";
    public const string ManageMembers = "ManageMembers";
    public const string RegisterDocuments = "RegisterDocuments";
    public const string UploadVersions = "UploadVersions";
    public const string SubmitForReview = "SubmitForReview";
    public const string Review = "Review";
    public const string Release = "Release";
    public const string ViewAudit = "ViewAudit";
    public const string ManageWatchedFolders = "ManageWatchedFolders";
}
