using DrawingFlow.Domain.Entities;
using DrawingFlow.Domain.Enums;
using DrawingFlow.Domain.Exceptions;

namespace DrawingFlow.UnitTests.Domain;

public sealed class EntityTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Organization_create_normalizes_code_and_sets_defaults()
    {
        var owner = Guid.NewGuid();
        var organization = Organization.Create(" acme ", "  ООО «Акме»  ", owner, Now);

        Assert.Equal("acme", organization.Code);
        Assert.Equal("ACME", organization.NormalizedCode);
        Assert.Equal("ООО «Акме»", organization.Name);
        Assert.True(organization.IsActive);
        Assert.Equal(owner, organization.CreatedByUserId);
        Assert.NotEqual(Guid.Empty, organization.Id);
    }

    [Fact]
    public void Project_update_changes_fields_and_timestamp()
    {
        var project = Project.Create(Guid.NewGuid(), "P-1", "Проект", null, Guid.NewGuid(), Now);
        var later = Now.AddHours(1);

        project.Update("Новое имя", "Описание", ProjectStatus.Archived, later);

        Assert.Equal("Новое имя", project.Name);
        Assert.Equal("Описание", project.Description);
        Assert.Equal(ProjectStatus.Archived, project.Status);
        Assert.Equal(later, project.UpdatedAtUtc);
        Assert.Equal(Now, project.CreatedAtUtc);
    }

    [Fact]
    public void Project_update_rejects_undefined_status()
    {
        var project = Project.Create(Guid.NewGuid(), "P-1", "Проект", null, Guid.NewGuid(), Now);
        Assert.Throws<ValidationException>(() => project.Update("x", null, (ProjectStatus)99, Now));
    }

    [Fact]
    public void User_requires_password_hash_and_valid_login()
    {
        Assert.Throws<ArgumentException>(() => User.Create("ivanov", "Иванов", null, "", false, Now));
        Assert.Throws<ValidationException>(() => User.Create("иванов", "Иванов", null, "hash", false, Now));

        var user = User.Create("Ivanov", "Иванов", "i@example.com", "hash", false, Now);
        Assert.Equal("IVANOV", user.NormalizedLogin);
        Assert.True(user.IsActive);
    }

    [Fact]
    public void User_deactivate_twice_is_a_rule_violation()
    {
        var user = User.Create("ivanov", "Иванов", null, "hash", false, Now);
        user.Deactivate();
        Assert.False(user.IsActive);
        Assert.Throws<DomainRuleException>(user.Deactivate);
        user.Activate();
        Assert.Throws<DomainRuleException>(user.Activate);
    }

    [Fact]
    public void RefreshToken_lifecycle()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), "hash", Now, TimeSpan.FromDays(30), "Desktop");

        Assert.True(token.IsActive(Now));
        Assert.True(token.IsActive(Now.AddDays(29)));
        Assert.False(token.IsActive(Now.AddDays(30)));

        token.Revoke(Now.AddMinutes(1), "next");
        Assert.False(token.IsActive(Now.AddMinutes(2)));
        Assert.Equal("next", token.ReplacedByTokenHash);

        // Повторный отзыв не перезаписывает сведения о первом.
        token.Revoke(Now.AddMinutes(5));
        Assert.Equal(Now.AddMinutes(1), token.RevokedAtUtc);
        Assert.Equal("next", token.ReplacedByTokenHash);
    }

    [Fact]
    public void Entities_reject_non_utc_timestamps()
    {
        var local = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);
        Assert.Throws<ArgumentException>(() => Organization.Create("A", "A", Guid.NewGuid(), local));
        Assert.Throws<ArgumentException>(() => AuditEvent.Create(local, "a", "b", null, "actor"));
    }

    [Fact]
    public void AuditEvent_truncates_client_address()
    {
        var evt = AuditEvent.Create(Now, "user.logged_in", "User", null, "actor", clientAddress: new string('1', 200));
        Assert.Equal(AuditEvent.ClientAddressMaxLength, evt.ClientAddress!.Length);
    }
}
