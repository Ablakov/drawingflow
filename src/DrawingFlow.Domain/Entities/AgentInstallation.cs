using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Exceptions;

namespace DrawingFlow.Domain.Entities;

/// <summary>
/// Установка агента на рабочей станции. Каждая установка имеет индивидуальный отзываемый ключ;
/// на сервере хранится только его хеш. Агент действует от имени организации с правами, ограниченными папками наблюдения.
/// </summary>
public sealed class AgentInstallation
{
    public const int NameMaxLength = 200;
    public const int MachineNameMaxLength = 200;
    public const int VersionMaxLength = 50;
    public const int CredentialPrefixLength = 8;

    /// <summary>Агент считается на связи, если последнее сердцебиение было не позже этого интервала.</summary>
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(3);

    private AgentInstallation() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? MachineName { get; private set; }
    public string? AgentVersion { get; private set; }
    public string CredentialHash { get; private set; } = string.Empty;
    /// <summary>Первые символы ключа — для идентификации установки оператором без раскрытия ключа.</summary>
    public string CredentialPrefix { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public Guid? RevokedByUserId { get; private set; }
    public DateTime? LastSeenAtUtc { get; private set; }
    public DateTime? LastHeartbeatAtUtc { get; private set; }
    public int PendingOperations { get; private set; }
    public int FailedOperations { get; private set; }
    public int UnresolvedOperations { get; private set; }

    public Organization? Organization { get; private set; }
    public ICollection<WatchedFolder> Folders { get; private set; } = new List<WatchedFolder>();

    public bool IsRevoked => RevokedAtUtc is not null;

    public bool IsOnline(DateTime nowUtc) => !IsRevoked && LastHeartbeatAtUtc is DateTime last && nowUtc - last <= OnlineWindow;

    public static AgentInstallation Create(Guid organizationId, string name, string credentialHash, string credentialPrefix, Guid createdByUserId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(credentialHash))
        {
            throw new ArgumentException("Хеш ключа агента обязателен.", nameof(credentialHash));
        }

        return new AgentInstallation
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            Name = Guard.Required(name, "name", "Название агента", NameMaxLength),
            CredentialHash = credentialHash,
            CredentialPrefix = credentialPrefix.Length > CredentialPrefixLength ? credentialPrefix[..CredentialPrefixLength] : credentialPrefix,
            CreatedAtUtc = Guard.Utc(nowUtc, nameof(nowUtc)),
            CreatedByUserId = createdByUserId,
        };
    }

    public void Revoke(Guid revokedByUserId, DateTime nowUtc)
    {
        if (IsRevoked)
        {
            throw new DomainRuleException("Ключ агента уже отозван.");
        }

        RevokedAtUtc = Guard.Utc(nowUtc, nameof(nowUtc));
        RevokedByUserId = revokedByUserId;
    }

    /// <summary>Фиксирует обращение агента (обмен ключа на токен).</summary>
    public void Touch(DateTime nowUtc, string? machineName, string? agentVersion)
    {
        LastSeenAtUtc = Guard.Utc(nowUtc, nameof(nowUtc));
        if (!string.IsNullOrWhiteSpace(machineName))
        {
            MachineName = Guard.Optional(machineName, "machineName", "Имя компьютера", MachineNameMaxLength);
        }

        if (!string.IsNullOrWhiteSpace(agentVersion))
        {
            AgentVersion = Guard.Optional(agentVersion, "agentVersion", "Версия агента", VersionMaxLength);
        }
    }

    public void RecordHeartbeat(DateTime nowUtc, string? machineName, string? agentVersion, int pending, int failed, int unresolved)
    {
        Touch(nowUtc, machineName, agentVersion);
        LastHeartbeatAtUtc = nowUtc;
        PendingOperations = Math.Max(0, pending);
        FailedOperations = Math.Max(0, failed);
        UnresolvedOperations = Math.Max(0, unresolved);
    }
}
