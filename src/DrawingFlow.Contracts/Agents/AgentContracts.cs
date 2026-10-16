namespace DrawingFlow.Contracts.Agents;

// Значения совпадают с DrawingFlow.Domain.Enums (тест паритета).
public enum FileMatchingRule
{
    NumberFromFileName = 0,
    FileNameIsNumber = 1,
    VersionFileName = 2,
}

public enum SyncOperationStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    NeedsResolution = 4,
    Cancelled = 5,
    Dismissed = 6,
}

public enum SyncRequestedAction
{
    None = 0,
    Retry = 1,
    UploadToDocument = 2,
    Dismiss = 3,
}

// ---------- Агенты (администрирование) ----------

public sealed record AgentDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string? MachineName,
    string? AgentVersion,
    string CredentialPrefix,
    bool IsRevoked,
    bool IsOnline,
    DateTime CreatedAtUtc,
    string CreatedByName,
    DateTime? RevokedAtUtc,
    DateTime? LastSeenAtUtc,
    DateTime? LastHeartbeatAtUtc,
    int PendingOperations,
    int FailedOperations,
    int UnresolvedOperations,
    int FolderCount);

public sealed record RegisterAgentRequest(string Name);

/// <summary>Ключ агента возвращается один раз при регистрации и больше не восстанавливается.</summary>
public sealed record AgentRegistrationResultDto(AgentDto Agent, string AgentKey);

// ---------- Аутентификация агента ----------

public sealed record AgentTokenRequest(string AgentKey, string? MachineName = null, string? AgentVersion = null);

public sealed record AgentTokenResponse(string AccessToken, DateTime ExpiresAtUtc, Guid AgentId, string AgentName, Guid OrganizationId);

// ---------- Папки наблюдения ----------

public sealed record WatchedFolderDto(
    Guid Id,
    Guid AgentId,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string Path,
    string IncludePatterns,
    string? ExcludePatterns,
    FileMatchingRule MatchingRule,
    string? DocumentNumberPattern,
    bool AutoRegisterUnknown,
    bool IsEnabled,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record SaveWatchedFolderRequest(
    Guid ProjectId,
    string Path,
    string? IncludePatterns = "*",
    string? ExcludePatterns = null,
    FileMatchingRule MatchingRule = FileMatchingRule.NumberFromFileName,
    string? DocumentNumberPattern = null,
    bool AutoRegisterUnknown = false,
    bool IsEnabled = true);

/// <summary>Конфигурация, которую агент получает с сервера.</summary>
public sealed record AgentConfigurationDto(
    Guid AgentId,
    string Name,
    Guid OrganizationId,
    string OrganizationName,
    DateTime ServerTimeUtc,
    IReadOnlyList<WatchedFolderDto> Folders);

public sealed record AgentHeartbeatRequest(
    string? MachineName,
    string? AgentVersion,
    int PendingOperations,
    int FailedOperations,
    int UnresolvedOperations);

// ---------- Операции синхронизации ----------

public sealed record SyncCandidateDto(Guid DocumentId, string DocumentNumber, string Title);

public sealed record ReportSyncOperationRequest(
    Guid OperationId,
    Guid? WatchedFolderId,
    Guid ProjectId,
    string FilePath,
    string FileName,
    long? FileSize,
    string? Sha256Hash,
    SyncOperationStatus Status,
    int RetryCount,
    string? LastError,
    Guid? DocumentId,
    Guid? DocumentVersionId,
    DateTime DetectedAtUtc,
    IReadOnlyList<SyncCandidateDto>? Candidates = null);

/// <summary>Действие пользователя, которое агент должен применить к своей локальной операции.</summary>
public sealed record AgentActionDto(Guid OperationId, SyncRequestedAction Action, Guid? DocumentId);

public sealed record SyncOperationDto(
    Guid Id,
    Guid AgentId,
    string AgentName,
    Guid? WatchedFolderId,
    string? FolderPath,
    Guid ProjectId,
    string ProjectCode,
    string FilePath,
    string FileName,
    long? FileSize,
    string? Sha256Hash,
    SyncOperationStatus Status,
    DateTime DetectedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc,
    int RetryCount,
    string? LastError,
    Guid? DocumentId,
    string? DocumentNumber,
    Guid? DocumentVersionId,
    int? VersionNumber,
    IReadOnlyList<SyncCandidateDto> Candidates,
    SyncRequestedAction RequestedAction,
    Guid? RequestedDocumentId);

public sealed record SyncOperationListQuery(
    Guid? AgentId = null,
    Guid? ProjectId = null,
    SyncOperationStatus? Status = null,
    // Только незавершённые (ожидают, выполняются, ошибки, требуют разрешения).
    bool ActiveOnly = false,
    int Page = 1,
    int PageSize = 50);

public sealed record ResolveSyncOperationRequest(Guid DocumentId);

/// <summary>Сводка состояния синхронизации организации для главной страницы.</summary>
public sealed record SyncSummaryDto(
    int AgentsTotal,
    int AgentsOnline,
    int AgentsRevoked,
    int PendingOperations,
    int FailedOperations,
    int UnresolvedOperations,
    DateTime? LastHeartbeatAtUtc);
