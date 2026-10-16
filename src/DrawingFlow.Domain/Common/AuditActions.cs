namespace DrawingFlow.Domain.Common;

/// <summary>Коды действий для журнала аудита. Хранятся в БД как строки, поэтому не переименовывать.</summary>
public static class AuditActions
{
    public const string UserLoggedIn = "user.logged_in";
    public const string UserLoginFailed = "user.login_failed";
    public const string UserLoggedOut = "user.logged_out";
    public const string UserCreated = "user.created";
    public const string UserPasswordChanged = "user.password_changed";
    public const string UserDeactivated = "user.deactivated";
    public const string UserActivated = "user.activated";

    public const string OrganizationCreated = "organization.created";
    public const string OrganizationUpdated = "organization.updated";
    public const string OrganizationMemberAdded = "organization.member_added";
    public const string OrganizationMemberRoleChanged = "organization.member_role_changed";
    public const string OrganizationMemberRemoved = "organization.member_removed";

    public const string ProjectCreated = "project.created";
    public const string ProjectUpdated = "project.updated";
    public const string ProjectMemberAdded = "project.member_added";
    public const string ProjectMemberRoleChanged = "project.member_role_changed";
    public const string ProjectMemberRemoved = "project.member_removed";

    public const string DocumentRegistered = "document.registered";
    public const string DocumentUpdated = "document.updated";
    public const string DocumentArchived = "document.archived";
    public const string DocumentRestored = "document.restored";
    public const string DocumentVersionUploaded = "document.version_uploaded";
    public const string DocumentVersionDownloaded = "document.version_downloaded";

    public const string AgentRegistered = "agent.registered";
    public const string AgentRevoked = "agent.revoked";
    public const string WatchedFolderCreated = "watched_folder.created";
    public const string WatchedFolderUpdated = "watched_folder.updated";
    public const string WatchedFolderDeleted = "watched_folder.deleted";
    public const string SyncOperationResolved = "sync_operation.resolved";
    public const string SyncOperationRetried = "sync_operation.retried";
    public const string SyncOperationDismissed = "sync_operation.dismissed";

    public const string DocumentSubmittedForReview = "document.submitted_for_review";
    public const string DocumentApproved = "document.approved";
    public const string DocumentRevisionRequested = "document.revision_requested";
    public const string DocumentReleased = "document.released";
    public const string WorkflowTaskCancelled = "workflow_task.cancelled";
}

/// <summary>Типы сущностей для журнала аудита.</summary>
public static class AuditEntityTypes
{
    public const string User = "User";
    public const string Organization = "Organization";
    public const string Project = "Project";
    public const string Document = "Document";
    public const string DocumentVersion = "DocumentVersion";
    public const string Agent = "Agent";
    public const string WatchedFolder = "WatchedFolder";
    public const string SyncOperation = "SyncOperation";
    public const string Review = "Review";
    public const string WorkflowTask = "WorkflowTask";
}
