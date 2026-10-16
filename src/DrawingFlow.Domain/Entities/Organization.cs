using DrawingFlow.Domain.Common;

namespace DrawingFlow.Domain.Entities;

public sealed class Organization
{
    public const int NameMaxLength = 200;

    private Organization() { }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NormalizedCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    public ICollection<OrganizationMembership> Memberships { get; private set; } = new List<OrganizationMembership>();
    public ICollection<Project> Projects { get; private set; } = new List<Project>();

    public static Organization Create(string code, string name, Guid createdByUserId, DateTime nowUtc)
    {
        var validCode = Guard.Code(code, "code", "Код организации");
        return new Organization
        {
            Id = Guid.CreateVersion7(),
            Code = validCode,
            NormalizedCode = Guard.NormalizeCode(validCode),
            Name = Guard.Required(name, "name", "Название организации", NameMaxLength),
            IsActive = true,
            CreatedAtUtc = Guard.Utc(nowUtc, nameof(nowUtc)),
            CreatedByUserId = createdByUserId,
        };
    }

    public void Rename(string name) => Name = Guard.Required(name, "name", "Название организации", NameMaxLength);

    public void SetActive(bool isActive) => IsActive = isActive;
}
