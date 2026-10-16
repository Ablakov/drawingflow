using DrawingFlow.Domain.Common;

namespace DrawingFlow.Domain.Entities;

/// <summary>Запись журнала аудита. Записи неизменяемы: только добавление.</summary>
public sealed class AuditEvent
{
    public const int ActorNameMaxLength = 200;
    public const int ActionMaxLength = 100;
    public const int EntityTypeMaxLength = 100;
    public const int EntityIdMaxLength = 100;
    public const int ClientAddressMaxLength = 64;

    private AuditEvent() { }

    public long Id { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public Guid? ActorAgentId { get; private set; }
    public string ActorName { get; private set; } = string.Empty;
    public Guid? OrganizationId { get; private set; }
    public Guid? ProjectId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string? EntityId { get; private set; }
    /// <summary>Дополнительные сведения в формате JSON (без секретов).</summary>
    public string? Details { get; private set; }
    public string? ClientAddress { get; private set; }

    public static AuditEvent Create(
        DateTime nowUtc,
        string action,
        string entityType,
        string? entityId,
        string actorName,
        Guid? actorUserId = null,
        Guid? actorAgentId = null,
        Guid? organizationId = null,
        Guid? projectId = null,
        string? detailsJson = null,
        string? clientAddress = null)
    {
        return new AuditEvent
        {
            OccurredAtUtc = Guard.Utc(nowUtc, nameof(nowUtc)),
            Action = Guard.Required(action, "action", "Действие", ActionMaxLength),
            EntityType = Guard.Required(entityType, "entityType", "Тип объекта", EntityTypeMaxLength),
            EntityId = Guard.Optional(entityId, "entityId", "Идентификатор объекта", EntityIdMaxLength),
            ActorName = Guard.Required(actorName, "actorName", "Инициатор", ActorNameMaxLength),
            ActorUserId = actorUserId,
            ActorAgentId = actorAgentId,
            OrganizationId = organizationId,
            ProjectId = projectId,
            Details = detailsJson,
            ClientAddress = clientAddress is null ? null : clientAddress[..Math.Min(clientAddress.Length, ClientAddressMaxLength)],
        };
    }
}
