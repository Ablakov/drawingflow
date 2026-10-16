using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Enums;

namespace DrawingFlow.Domain.Entities;

public sealed class OrganizationMembership
{
    private OrganizationMembership() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public OrganizationRole Role { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public Organization? Organization { get; private set; }
    public User? User { get; private set; }

    public static OrganizationMembership Create(Guid organizationId, Guid userId, OrganizationRole role, DateTime nowUtc)
    {
        return new OrganizationMembership
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            UserId = userId,
            Role = role,
            CreatedAtUtc = Guard.Utc(nowUtc, nameof(nowUtc)),
        };
    }

    public void ChangeRole(OrganizationRole role) => Role = role;
}
