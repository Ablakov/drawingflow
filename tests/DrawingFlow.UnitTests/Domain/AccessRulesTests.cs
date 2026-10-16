using DrawingFlow.Domain.Authorization;
using DrawingFlow.Domain.Enums;

namespace DrawingFlow.UnitTests.Domain;

public sealed class AccessRulesTests
{
    [Fact]
    public void Owner_has_every_organization_permission()
    {
        var permissions = AccessRules.PermissionsOf(OrganizationRole.Owner);
        foreach (var value in Enum.GetValues<OrganizationPermission>())
        {
            Assert.True(permissions.Has(value), $"Owner должен иметь {value}");
        }
    }

    [Fact]
    public void OrganizationAdmin_cannot_manage_settings_but_can_manage_members_and_projects()
    {
        var permissions = AccessRules.PermissionsOf(OrganizationRole.OrganizationAdmin);
        Assert.False(permissions.Has(OrganizationPermission.ManageSettings));
        Assert.True(permissions.Has(OrganizationPermission.ManageMembers));
        Assert.True(permissions.Has(OrganizationPermission.CreateProjects));
        Assert.True(permissions.Has(OrganizationPermission.ViewAllProjects));
        Assert.True(permissions.Has(OrganizationPermission.ViewAudit));
    }

    [Fact]
    public void Member_can_only_view_organization()
    {
        Assert.Equal(OrganizationPermission.View, AccessRules.PermissionsOf(OrganizationRole.Member));
    }

    [Theory]
    [InlineData(ProjectRole.Viewer, ProjectPermission.View, true)]
    [InlineData(ProjectRole.Viewer, ProjectPermission.UploadVersions, false)]
    [InlineData(ProjectRole.Reviewer, ProjectPermission.Review, true)]
    [InlineData(ProjectRole.Reviewer, ProjectPermission.UploadVersions, false)]
    [InlineData(ProjectRole.Engineer, ProjectPermission.UploadVersions, true)]
    [InlineData(ProjectRole.Engineer, ProjectPermission.Review, false)]
    [InlineData(ProjectRole.Engineer, ProjectPermission.ManageMembers, false)]
    [InlineData(ProjectRole.ProjectManager, ProjectPermission.ManageMembers, true)]
    [InlineData(ProjectRole.ProjectManager, ProjectPermission.Release, true)]
    [InlineData(ProjectRole.ProjectManager, ProjectPermission.Review, true)]
    public void Project_role_permissions(ProjectRole role, ProjectPermission permission, bool expected)
    {
        Assert.Equal(expected, AccessRules.PermissionsOf(role).Has(permission));
    }

    [Fact]
    public void Organization_admins_act_as_project_managers()
    {
        Assert.Equal(ProjectRole.ProjectManager, AccessRules.EffectiveProjectRole(OrganizationRole.Owner, null));
        Assert.Equal(ProjectRole.ProjectManager, AccessRules.EffectiveProjectRole(OrganizationRole.OrganizationAdmin, ProjectRole.Viewer));
    }

    [Fact]
    public void Member_without_project_membership_has_no_project_role()
    {
        Assert.Null(AccessRules.EffectiveProjectRole(OrganizationRole.Member, null));
        Assert.Equal(ProjectRole.Engineer, AccessRules.EffectiveProjectRole(OrganizationRole.Member, ProjectRole.Engineer));
    }

    [Fact]
    public void User_outside_organization_has_no_project_role_even_with_stale_membership()
    {
        Assert.Null(AccessRules.EffectiveProjectRole(null, ProjectRole.ProjectManager));
    }

    [Theory]
    [InlineData(OrganizationRole.Owner, OrganizationRole.Owner, true)]
    [InlineData(OrganizationRole.Owner, OrganizationRole.Member, true)]
    [InlineData(OrganizationRole.OrganizationAdmin, OrganizationRole.Owner, false)]
    [InlineData(OrganizationRole.OrganizationAdmin, OrganizationRole.OrganizationAdmin, true)]
    [InlineData(OrganizationRole.OrganizationAdmin, OrganizationRole.Member, true)]
    [InlineData(OrganizationRole.Member, OrganizationRole.Member, false)]
    public void Role_assignment_matrix(OrganizationRole actor, OrganizationRole target, bool expected)
    {
        Assert.Equal(expected, AccessRules.CanAssignOrganizationRole(actor, target));
    }
}
