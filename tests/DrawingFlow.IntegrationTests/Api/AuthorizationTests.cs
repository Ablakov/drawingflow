using System.Net;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Organizations;
using DrawingFlow.Contracts.Projects;
using DrawingFlow.Contracts.Users;
using DrawingFlow.IntegrationTests.Infrastructure;

namespace DrawingFlow.IntegrationTests.Api;

/// <summary>Изоляция организаций и проектов, защита от IDOR, проверка прав на сервере.</summary>
public sealed class AuthorizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Outsider_cannot_see_organization_or_project()
    {
        var s = await Scenario.CreateAsync(factory);

        Assert.Empty(await s.Outsider.GetOrganizationsAsync());
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Outsider.GetOrganizationAsync(s.Organization.Id));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Outsider.GetProjectsAsync(s.Organization.Id, new ProjectListQuery()));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Outsider.GetProjectAsync(s.Project.Id));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Outsider.GetProjectMembersAsync(s.Project.Id));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Outsider.GetOrganizationMembersAsync(s.Organization.Id));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Outsider.AddProjectMemberAsync(s.Project.Id, new AddProjectMemberRequest(s.Login("outsider"), ProjectRole.ProjectManager)));
    }

    [Fact]
    public async Task Member_without_project_role_does_not_see_project()
    {
        var s = await Scenario.CreateAsync(factory);

        var organizations = await s.Member.GetOrganizationsAsync();
        Assert.Single(organizations);
        Assert.Equal(OrganizationRole.Member, organizations[0].CurrentUserRole);
        Assert.Equal(new[] { OrganizationPermissions.View }, organizations[0].Permissions);

        var projects = await s.Member.GetProjectsAsync(s.Organization.Id, new ProjectListQuery());
        Assert.Empty(projects.Items);
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Member.GetProjectAsync(s.Project.Id));
    }

    [Fact]
    public async Task Viewer_can_read_but_not_change_project()
    {
        var s = await Scenario.CreateAsync(factory);

        var project = await s.Viewer.GetProjectAsync(s.Project.Id);
        Assert.Equal(ProjectRole.Viewer, project.CurrentUserRole);
        Assert.Equal(new[] { ProjectPermissions.View }, project.Permissions);
        Assert.Equal(4, (await s.Viewer.GetProjectMembersAsync(s.Project.Id)).Count);

        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Viewer.UpdateProjectAsync(s.Project.Id, new UpdateProjectRequest("Взлом", null, ProjectStatus.Active)));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Viewer.AddProjectMemberAsync(s.Project.Id, new AddProjectMemberRequest(s.Login("member"), ProjectRole.ProjectManager)));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Viewer.RemoveProjectMemberAsync(s.Project.Id, project.Id));
    }

    [Fact]
    public async Task Engineer_cannot_create_projects_or_view_audit()
    {
        var s = await Scenario.CreateAsync(factory);

        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Engineer.CreateProjectAsync(s.Organization.Id, new CreateProjectRequest("X-" + s.Suffix, "Нельзя")));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Engineer.GetAuditAsync(s.Organization.Id, new Contracts.Audit.AuditListQuery()));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Engineer.AddOrganizationMemberAsync(s.Organization.Id, new AddOrganizationMemberRequest(s.Login("outsider"), OrganizationRole.Member)));

        var project = await s.Engineer.GetProjectAsync(s.Project.Id);
        Assert.Contains(ProjectPermissions.UploadVersions, project.Permissions);
        Assert.DoesNotContain(ProjectPermissions.Review, project.Permissions);
    }

    [Fact]
    public async Task Project_manager_manages_members_but_not_organization()
    {
        var s = await Scenario.CreateAsync(factory);

        var added = await s.Manager.AddProjectMemberAsync(s.Project.Id, new AddProjectMemberRequest(s.Login("member"), ProjectRole.Viewer));
        Assert.Equal(ProjectRole.Viewer, added.Role);
        var changed = await s.Manager.UpdateProjectMemberAsync(s.Project.Id, added.UserId, new UpdateProjectMemberRequest(ProjectRole.Reviewer));
        Assert.Equal(ProjectRole.Reviewer, changed.Role);
        await s.Manager.RemoveProjectMemberAsync(s.Project.Id, added.UserId);

        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Manager.CreateProjectAsync(s.Organization.Id, new CreateProjectRequest("PM-" + s.Suffix, "Нельзя")));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Manager.AddOrganizationMemberAsync(s.Organization.Id, new AddOrganizationMemberRequest(s.Login("outsider"), OrganizationRole.Member)));
    }

    [Fact]
    public async Task Organization_admin_sees_all_projects_but_cannot_touch_settings_or_owners()
    {
        var s = await Scenario.CreateAsync(factory);

        var projects = await s.OrgAdmin.GetProjectsAsync(s.Organization.Id, new ProjectListQuery());
        Assert.Contains(projects.Items, p => p.Id == s.Project.Id && p.CurrentUserRole == ProjectRole.ProjectManager);

        var created = await s.OrgAdmin.CreateProjectAsync(s.Organization.Id, new CreateProjectRequest("ADM-" + s.Suffix, "Проект администратора"));
        Assert.Contains(ProjectPermissions.ManageMembers, created.Permissions);

        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.OrgAdmin.UpdateOrganizationAsync(s.Organization.Id, new UpdateOrganizationRequest("Новое имя")));
        var ex = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.OrgAdmin.AddOrganizationMemberAsync(s.Organization.Id, new AddOrganizationMemberRequest(s.Login("outsider"), OrganizationRole.Owner)));
        Assert.Equal("forbidden", ex.ErrorCode);

        var ownerId = (await s.OrgAdmin.GetOrganizationMembersAsync(s.Organization.Id)).Single(m => m.Role == OrganizationRole.Owner).UserId;
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.OrgAdmin.UpdateOrganizationMemberAsync(s.Organization.Id, ownerId, new UpdateOrganizationMemberRequest(OrganizationRole.Member)));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.OrgAdmin.RemoveOrganizationMemberAsync(s.Organization.Id, ownerId));
    }

    [Fact]
    public async Task Owner_can_update_organization_settings()
    {
        var s = await Scenario.CreateAsync(factory);
        var updated = await s.Owner.UpdateOrganizationAsync(s.Organization.Id, new UpdateOrganizationRequest("Переименованная " + s.Suffix));
        Assert.Equal("Переименованная " + s.Suffix, updated.Name);
        Assert.Contains(OrganizationPermissions.ManageSettings, updated.Permissions);
    }

    [Fact]
    public async Task Only_server_administrator_creates_organizations_and_lists_users()
    {
        var s = await Scenario.CreateAsync(factory);

        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Owner.CreateOrganizationAsync(new CreateOrganizationRequest("NEW-" + s.Suffix, "Нельзя")));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Owner.GetUsersAsync(null));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Owner.CreateUserAsync(new CreateUserRequest("free_" + s.Suffix, "Без организации", null, Scenario.Password)));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Owner.CreateUserAsync(new CreateUserRequest("sa_" + s.Suffix, "Админ", null, Scenario.Password, s.Organization.Id, OrganizationRole.Member, IsServerAdministrator: true)));

        var users = await s.Admin.GetUsersAsync(s.Suffix);
        Assert.True(users.TotalCount >= 8);
    }

    [Fact]
    public async Task Cross_organization_access_is_hidden_as_not_found()
    {
        var a = await Scenario.CreateAsync(factory);
        var b = await Scenario.CreateAsync(factory);

        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => a.Owner.GetProjectAsync(b.Project.Id));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => a.OrgAdmin.GetOrganizationMembersAsync(b.Organization.Id));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => a.OrgAdmin.CreateProjectAsync(b.Organization.Id, new CreateProjectRequest("HACK", "Чужая организация")));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => a.Manager.AddProjectMemberAsync(b.Project.Id, new AddProjectMemberRequest(a.Login("engineer"), ProjectRole.Viewer)));
        // Участник чужой организации не может быть добавлен в проект: существование пользователя не раскрывается.
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => a.Manager.AddProjectMemberAsync(a.Project.Id, new AddProjectMemberRequest(b.Login("engineer"), ProjectRole.Viewer)));
        // Аудит чужого проекта недоступен даже при указании своей организации.
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => a.Owner.GetAuditAsync(a.Organization.Id, new Contracts.Audit.AuditListQuery(ProjectId: b.Project.Id)));
    }

    [Fact]
    public async Task Server_administrator_acts_as_owner_everywhere()
    {
        var s = await Scenario.CreateAsync(factory);
        var organization = await s.Admin.GetOrganizationAsync(s.Organization.Id);
        Assert.Equal(OrganizationRole.Owner, organization.CurrentUserRole);
        var me = await s.Admin.GetCurrentUserAsync();
        Assert.Contains(me.Organizations, o => o.Id == s.Organization.Id && o.Role == OrganizationRole.Owner);
    }
}
