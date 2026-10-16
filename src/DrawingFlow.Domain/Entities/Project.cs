using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Enums;
using DrawingFlow.Domain.Exceptions;

namespace DrawingFlow.Domain.Entities;

public sealed class Project
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 4000;

    private Project() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NormalizedCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public ProjectStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public Organization? Organization { get; private set; }
    public ICollection<ProjectMembership> Memberships { get; private set; } = new List<ProjectMembership>();

    public static Project Create(Guid organizationId, string code, string name, string? description, Guid createdByUserId, DateTime nowUtc)
    {
        var validCode = Guard.Code(code, "code", "Код проекта");
        Guard.Utc(nowUtc, nameof(nowUtc));
        return new Project
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            Code = validCode,
            NormalizedCode = Guard.NormalizeCode(validCode),
            Name = Guard.Required(name, "name", "Название проекта", NameMaxLength),
            Description = Guard.Optional(description, "description", "Описание", DescriptionMaxLength),
            Status = ProjectStatus.Active,
            CreatedAtUtc = nowUtc,
            CreatedByUserId = createdByUserId,
            UpdatedAtUtc = nowUtc,
        };
    }

    public void Update(string name, string? description, ProjectStatus status, DateTime nowUtc)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ValidationException("status", "Недопустимый статус проекта.");
        }

        Name = Guard.Required(name, "name", "Название проекта", NameMaxLength);
        Description = Guard.Optional(description, "description", "Описание", DescriptionMaxLength);
        Status = status;
        UpdatedAtUtc = Guard.Utc(nowUtc, nameof(nowUtc));
    }
}
