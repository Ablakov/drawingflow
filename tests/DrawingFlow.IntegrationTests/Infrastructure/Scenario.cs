using DrawingFlow.Client;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Organizations;
using DrawingFlow.Contracts.Projects;
using DrawingFlow.Contracts.Users;

namespace DrawingFlow.IntegrationTests.Infrastructure;

/// <summary>
/// Типовая конфигурация: организация с владельцем, администратором, участниками с разными ролями в проекте,
/// участником без проектов и посторонним пользователем (не в организации).
/// </summary>
public sealed class Scenario
{
    public const string Password = "Secret12345";

    public required string Suffix { get; init; }
    public required DrawingFlowApiClient Admin { get; init; }
    public required OrganizationDto Organization { get; init; }
    public required ProjectDto Project { get; init; }
    public required DrawingFlowApiClient Owner { get; init; }
    public required DrawingFlowApiClient OrgAdmin { get; init; }
    public required DrawingFlowApiClient Manager { get; init; }
    public required DrawingFlowApiClient Engineer { get; init; }
    public required DrawingFlowApiClient Reviewer { get; init; }
    public required DrawingFlowApiClient Viewer { get; init; }
    public required DrawingFlowApiClient Member { get; init; }
    public required DrawingFlowApiClient Outsider { get; init; }

    public string Login(string role) => $"{role}_{Suffix}";

    public static async Task<Scenario> CreateAsync(ApiFactory factory)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var admin = await factory.LoginAdminAsync();

        async Task<UserDto> CreateUser(string role, Guid? organizationId = null, OrganizationRole orgRole = OrganizationRole.Member) =>
            await admin.CreateUserAsync(new CreateUserRequest($"{role}_{suffix}", $"{role} {suffix}", null, Password, organizationId, organizationId is null ? null : orgRole, RequirePasswordChange: false));

        var ownerUser = await CreateUser("owner");
        var organization = await admin.CreateOrganizationAsync(new CreateOrganizationRequest($"ORG-{suffix}", $"Организация {suffix}", ownerUser.Login));

        await CreateUser("orgadmin", organization.Id, OrganizationRole.OrganizationAdmin);
        await CreateUser("manager", organization.Id);
        await CreateUser("engineer", organization.Id);
        await CreateUser("reviewer", organization.Id);
        await CreateUser("viewer", organization.Id);
        await CreateUser("member", organization.Id);
        await CreateUser("outsider");

        var project = await admin.CreateProjectAsync(organization.Id, new CreateProjectRequest($"PRJ-{suffix}", $"Проект {suffix}", "Тестовый проект"));
        await admin.AddProjectMemberAsync(project.Id, new AddProjectMemberRequest($"manager_{suffix}", ProjectRole.ProjectManager));
        await admin.AddProjectMemberAsync(project.Id, new AddProjectMemberRequest($"engineer_{suffix}", ProjectRole.Engineer));
        await admin.AddProjectMemberAsync(project.Id, new AddProjectMemberRequest($"reviewer_{suffix}", ProjectRole.Reviewer));
        await admin.AddProjectMemberAsync(project.Id, new AddProjectMemberRequest($"viewer_{suffix}", ProjectRole.Viewer));

        return new Scenario
        {
            Suffix = suffix,
            Admin = admin,
            Organization = organization,
            Project = project,
            Owner = await factory.LoginAsync($"owner_{suffix}", Password),
            OrgAdmin = await factory.LoginAsync($"orgadmin_{suffix}", Password),
            Manager = await factory.LoginAsync($"manager_{suffix}", Password),
            Engineer = await factory.LoginAsync($"engineer_{suffix}", Password),
            Reviewer = await factory.LoginAsync($"reviewer_{suffix}", Password),
            Viewer = await factory.LoginAsync($"viewer_{suffix}", Password),
            Member = await factory.LoginAsync($"member_{suffix}", Password),
            Outsider = await factory.LoginAsync($"outsider_{suffix}", Password),
        };
    }
}

public static class ApiAssert
{
    public static async Task<ApiException> ThrowsStatusAsync(System.Net.HttpStatusCode status, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<ApiException>(action);
        Assert.Equal(status, ex.StatusCode);
        return ex;
    }
}
