using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Enums;

namespace DrawingFlow.Domain.Entities;

public sealed class ProjectMembership
{
    private ProjectMembership() { }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid UserId { get; private set; }
    public ProjectRole Role { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public Project? Project { get; private set; }
    public User? User { get; private set; }

    public static ProjectMembership Create(Guid projectId, Guid userId, ProjectRole role, DateTime nowUtc)
    {
        return new ProjectMembership
        {
            Id = Guid.CreateVersion7(),
            ProjectId = projectId,
            UserId = userId,
            Role = role,
            CreatedAtUtc = Guard.Utc(nowUtc, nameof(nowUtc)),
        };
    }

    public void ChangeRole(ProjectRole role) => Role = role;
}
