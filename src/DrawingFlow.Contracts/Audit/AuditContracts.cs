namespace DrawingFlow.Contracts.Audit;

public sealed record AuditEventDto(
    long Id,
    DateTime OccurredAtUtc,
    string ActorName,
    Guid? ActorUserId,
    Guid? ActorAgentId,
    string Action,
    string EntityType,
    string? EntityId,
    Guid? OrganizationId,
    Guid? ProjectId,
    string? Details);

public sealed record AuditListQuery(
    Guid? ProjectId = null,
    string? Action = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int Page = 1,
    int PageSize = 50);
