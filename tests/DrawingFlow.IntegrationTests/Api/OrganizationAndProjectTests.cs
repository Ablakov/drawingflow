using System.Net;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Audit;
using DrawingFlow.Contracts.Organizations;
using DrawingFlow.Contracts.Projects;
using DrawingFlow.Contracts.Users;
using DrawingFlow.IntegrationTests.Infrastructure;

namespace DrawingFlow.IntegrationTests.Api;

public sealed class OrganizationAndProjectTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Organization_code_is_unique_case_insensitively_and_validated()
    {
        var admin = await factory.LoginAdminAsync();
        var code = "DUP-" + Guid.NewGuid().ToString("N")[..6];
        await admin.CreateOrganizationAsync(new CreateOrganizationRequest(code, "Первая"));

        var conflict = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Conflict, () => admin.CreateOrganizationAsync(new CreateOrganizationRequest(code.ToLowerInvariant(), "Вторая")));
        Assert.Equal("Организация с таким кодом уже существует.", conflict.Message);

        var validation = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.BadRequest, () => admin.CreateOrganizationAsync(new CreateOrganizationRequest("плохой код", "X")));
        Assert.Equal("validation", validation.ErrorCode);
        Assert.True(validation.Errors!.ContainsKey("code"));

        var missingName = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.BadRequest, () => admin.CreateOrganizationAsync(new CreateOrganizationRequest("OK-" + Guid.NewGuid().ToString("N")[..4], "   ")));
        Assert.True(missingName.Errors!.ContainsKey("name"));
    }

    [Fact]
    public async Task Membership_rules_are_enforced()
    {
        var s = await Scenario.CreateAsync(factory);

        var unknown = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Owner.AddOrganizationMemberAsync(s.Organization.Id, new AddOrganizationMemberRequest("no_such_user_" + s.Suffix, OrganizationRole.Member)));
        Assert.Equal("Пользователь с таким логином не найден.", unknown.Message);

        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Conflict, () => s.Owner.AddOrganizationMemberAsync(s.Organization.Id, new AddOrganizationMemberRequest(s.Login("engineer"), OrganizationRole.Member)));

        var members = await s.Owner.GetOrganizationMembersAsync(s.Organization.Id);
        var owner = members.Single(m => m.Role == OrganizationRole.Owner);
        var lastOwner = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.UnprocessableEntity, () => s.Owner.UpdateOrganizationMemberAsync(s.Organization.Id, owner.UserId, new UpdateOrganizationMemberRequest(OrganizationRole.Member)));
        Assert.Equal("domain_rule", lastOwner.ErrorCode);
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.UnprocessableEntity, () => s.Owner.RemoveOrganizationMemberAsync(s.Organization.Id, owner.UserId));

        // Второй владелец — теперь первого можно понизить.
        var orgAdmin = members.Single(m => m.Role == OrganizationRole.OrganizationAdmin);
        await s.Owner.UpdateOrganizationMemberAsync(s.Organization.Id, orgAdmin.UserId, new UpdateOrganizationMemberRequest(OrganizationRole.Owner));
        var demoted = await s.Owner.UpdateOrganizationMemberAsync(s.Organization.Id, owner.UserId, new UpdateOrganizationMemberRequest(OrganizationRole.OrganizationAdmin));
        Assert.Equal(OrganizationRole.OrganizationAdmin, demoted.Role);
    }

    [Fact]
    public async Task Removing_organization_member_removes_project_memberships()
    {
        var s = await Scenario.CreateAsync(factory);
        var engineerId = (await s.Owner.GetProjectMembersAsync(s.Project.Id)).Single(m => m.Role == ProjectRole.Engineer).UserId;

        await s.Owner.RemoveOrganizationMemberAsync(s.Organization.Id, engineerId);

        Assert.DoesNotContain(await s.Owner.GetProjectMembersAsync(s.Project.Id), m => m.UserId == engineerId);
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Engineer.GetProjectAsync(s.Project.Id));
        Assert.Empty(await s.Engineer.GetOrganizationsAsync());
    }

    [Fact]
    public async Task Organization_admin_creates_users_inside_own_organization_only()
    {
        var s = await Scenario.CreateAsync(factory);
        var login = "new_" + s.Suffix;

        var user = await s.OrgAdmin.CreateUserAsync(new CreateUserRequest(login, "Новый сотрудник", "new@example.com", Scenario.Password, s.Organization.Id, OrganizationRole.Member, RequirePasswordChange: false));
        Assert.False(user.IsServerAdministrator);
        Assert.Contains(await s.OrgAdmin.GetOrganizationMembersAsync(s.Organization.Id), m => m.Login == login && m.Role == OrganizationRole.Member);

        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Conflict, () => s.OrgAdmin.CreateUserAsync(new CreateUserRequest(login.ToUpperInvariant(), "Дубль", null, Scenario.Password, s.Organization.Id)));
        var weak = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.BadRequest, () => s.OrgAdmin.CreateUserAsync(new CreateUserRequest("weak_" + s.Suffix, "Слабый пароль", null, "short", s.Organization.Id)));
        Assert.True(weak.Errors!.ContainsKey("password"));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.OrgAdmin.CreateUserAsync(new CreateUserRequest("owner2_" + s.Suffix, "Владелец", null, Scenario.Password, s.Organization.Id, OrganizationRole.Owner)));

        var fresh = await factory.LoginAsync(login, Scenario.Password);
        Assert.Single(await fresh.GetOrganizationsAsync());
    }

    [Fact]
    public async Task Projects_support_search_sort_and_paging()
    {
        var s = await Scenario.CreateAsync(factory);
        for (var i = 1; i <= 5; i++)
        {
            await s.Owner.CreateProjectAsync(s.Organization.Id, new CreateProjectRequest($"ЖД-{i}".Replace("ЖД", "ZD") + "-" + s.Suffix, $"Жилой дом {i}", null));
        }

        await s.Owner.CreateProjectAsync(s.Organization.Id, new CreateProjectRequest("SCHOOL-" + s.Suffix, "Школа на 600 мест", null));

        var search = await s.Owner.GetProjectsAsync(s.Organization.Id, new ProjectListQuery(Search: "ЖИЛОЙ дом"));
        Assert.Equal(5, search.TotalCount);
        Assert.All(search.Items, p => Assert.StartsWith("Жилой дом", p.Name));

        var page = await s.Owner.GetProjectsAsync(s.Organization.Id, new ProjectListQuery(Search: "жилой", Sort: ProjectSortField.Code, Descending: true, Page: 2, PageSize: 2));
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(3, page.TotalPages);
        Assert.True(page.HasNext && page.HasPrevious);
        Assert.Equal("ZD-3-" + s.Suffix, page.Items[0].Code);

        var archived = await s.Owner.UpdateProjectAsync(search.Items[0].Id, new UpdateProjectRequest(search.Items[0].Name, "В архиве", ProjectStatus.Archived));
        Assert.Equal(ProjectStatus.Archived, archived.Status);
        var onlyArchived = await s.Owner.GetProjectsAsync(s.Organization.Id, new ProjectListQuery(Status: ProjectStatus.Archived));
        Assert.Single(onlyArchived.Items);

        var huge = await s.Owner.GetProjectsAsync(s.Organization.Id, new ProjectListQuery(PageSize: 10_000, Page: 0));
        Assert.Equal(PageRequest.MaxPageSize, huge.PageSize);
        Assert.Equal(1, huge.Page);
    }

    [Fact]
    public async Task Project_code_is_unique_within_organization_even_under_concurrency()
    {
        var s = await Scenario.CreateAsync(factory);
        var code = "RACE-" + s.Suffix;

        var attempts = await Task.WhenAll(Enumerable.Range(0, 4).Select(async i =>
        {
            try
            {
                await s.Owner.CreateProjectAsync(s.Organization.Id, new CreateProjectRequest(code, $"Гонка {i}"));
                return HttpStatusCode.Created;
            }
            catch (Client.ApiException ex)
            {
                return ex.StatusCode;
            }
        }));

        Assert.Equal(1, attempts.Count(a => a == HttpStatusCode.Created));
        Assert.Equal(3, attempts.Count(a => a == HttpStatusCode.Conflict));
        var projects = await s.Owner.GetProjectsAsync(s.Organization.Id, new ProjectListQuery(Search: code));
        Assert.Single(projects.Items);

        // Тот же код в другой организации допустим.
        var other = await Scenario.CreateAsync(factory);
        await other.Owner.CreateProjectAsync(other.Organization.Id, new CreateProjectRequest(code, "Другая организация"));
    }

    [Fact]
    public async Task Project_member_operations()
    {
        var s = await Scenario.CreateAsync(factory);

        var notInOrg = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Owner.AddProjectMemberAsync(s.Project.Id, new AddProjectMemberRequest(s.Login("outsider"), ProjectRole.Viewer)));
        Assert.Contains("участников организации", notInOrg.Message);
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Conflict, () => s.Owner.AddProjectMemberAsync(s.Project.Id, new AddProjectMemberRequest(s.Login("engineer"), ProjectRole.Viewer)));

        var members = await s.Owner.GetProjectMembersAsync(s.Project.Id);
        var engineer = members.Single(m => m.Role == ProjectRole.Engineer);
        var promoted = await s.Owner.UpdateProjectMemberAsync(s.Project.Id, engineer.UserId, new UpdateProjectMemberRequest(ProjectRole.ProjectManager));
        Assert.Equal(ProjectRole.ProjectManager, promoted.Role);

        var asEngineer = await s.Engineer.GetProjectAsync(s.Project.Id);
        Assert.Equal(ProjectRole.ProjectManager, asEngineer.CurrentUserRole);
        Assert.Contains(ProjectPermissions.ManageMembers, asEngineer.Permissions);

        await s.Owner.RemoveProjectMemberAsync(s.Project.Id, engineer.UserId);
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Owner.RemoveProjectMemberAsync(s.Project.Id, engineer.UserId));
        Assert.Equal(3, (await s.Owner.GetProjectMembersAsync(s.Project.Id)).Count);
    }

    [Fact]
    public async Task Audit_records_actions_and_respects_permissions()
    {
        var s = await Scenario.CreateAsync(factory);

        var audit = await s.Owner.GetAuditAsync(s.Organization.Id, new AuditListQuery(PageSize: 100));
        Assert.Contains(audit.Items, e => e.Action == "organization.created");
        Assert.Contains(audit.Items, e => e.Action == "project.created" && e.ProjectId == s.Project.Id);
        Assert.Equal(4, audit.Items.Count(e => e.Action == "project.member_added" && e.ProjectId == s.Project.Id));
        Assert.All(audit.Items, e => Assert.Equal(s.Organization.Id, e.OrganizationId));

        var filtered = await s.OrgAdmin.GetAuditAsync(s.Organization.Id, new AuditListQuery(Action: "project.created"));
        Assert.All(filtered.Items, e => Assert.Equal("project.created", e.Action));
        Assert.True(filtered.TotalCount >= 1);

        // Руководитель проекта видит аудит своего проекта, но не всей организации.
        var projectAudit = await s.Manager.GetAuditAsync(s.Organization.Id, new AuditListQuery(ProjectId: s.Project.Id));
        Assert.All(projectAudit.Items, e => Assert.Equal(s.Project.Id, e.ProjectId));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Manager.GetAuditAsync(s.Organization.Id, new AuditListQuery()));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Viewer.GetAuditAsync(s.Organization.Id, new AuditListQuery(ProjectId: s.Project.Id)));

        var byDate = await s.Owner.GetAuditAsync(s.Organization.Id, new AuditListQuery(FromUtc: DateTime.UtcNow.AddMinutes(5)));
        Assert.Equal(0, byDate.TotalCount);
    }

    [Fact]
    public async Task Current_user_lists_organizations_with_roles()
    {
        var s = await Scenario.CreateAsync(factory);
        var me = await s.Engineer.GetCurrentUserAsync();
        Assert.Equal(s.Login("engineer"), me.User.Login);
        Assert.Single(me.Organizations);
        Assert.Equal(OrganizationRole.Member, me.Organizations[0].Role);

        var organizations = await s.OrgAdmin.GetOrganizationsAsync();
        var organization = Assert.Single(organizations);
        Assert.Equal(7, organization.MemberCount);
        Assert.Equal(1, organization.ProjectCount);
        Assert.Contains(OrganizationPermissions.CreateUsers, organization.Permissions);
    }
}
