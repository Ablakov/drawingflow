using DrawingFlow.Application.Abstractions;
using DrawingFlow.Application.Audit;
using DrawingFlow.Application.Authorization;
using DrawingFlow.Application.Common;
using Api = DrawingFlow.Contracts;
using DrawingFlow.Contracts.Projects;
using DrawingFlow.Domain.Authorization;
using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Entities;
using DrawingFlow.Domain.Enums;
using DrawingFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DrawingFlow.Application.Projects;

public sealed class ProjectService(
    IAppDbContext db,
    IClock clock,
    ICurrentUser currentUser,
    AccessService access,
    AuditService audit)
{
    private const string MemberNotFound = "Участник не найден в проекте.";

    public async Task<Api.PagedResult<ProjectDto>> ListAsync(Guid organizationId, ProjectListQuery query, CancellationToken cancellationToken)
    {
        var organizationAccess = await access.RequireOrganizationAsync(organizationId, OrganizationPermission.View, cancellationToken);
        var userId = currentUser.RequireUserId();
        var canViewAll = organizationAccess.Permissions.Has(OrganizationPermission.ViewAllProjects);

        IQueryable<Project> projects = db.Projects.AsNoTracking().Where(p => p.OrganizationId == organizationId);
        if (!canViewAll)
        {
            projects = projects.Where(p => p.Memberships.Any(m => m.UserId == userId));
        }

        if (query.Status is not null)
        {
            var status = query.Status.Value.ToDomain();
            projects = projects.Where(p => p.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            projects = projects.Where(p => p.Name.ToLower().Contains(term) || p.Code.ToLower().Contains(term));
        }

        projects = (query.Sort, query.Descending) switch
        {
            (ProjectSortField.Code, false) => projects.OrderBy(p => p.Code).ThenBy(p => p.Id),
            (ProjectSortField.Code, true) => projects.OrderByDescending(p => p.Code).ThenBy(p => p.Id),
            (ProjectSortField.UpdatedAt, false) => projects.OrderBy(p => p.UpdatedAtUtc).ThenBy(p => p.Id),
            (ProjectSortField.UpdatedAt, true) => projects.OrderByDescending(p => p.UpdatedAtUtc).ThenBy(p => p.Id),
            (ProjectSortField.CreatedAt, false) => projects.OrderBy(p => p.CreatedAtUtc).ThenBy(p => p.Id),
            (ProjectSortField.CreatedAt, true) => projects.OrderByDescending(p => p.CreatedAtUtc).ThenBy(p => p.Id),
            (_, true) => projects.OrderByDescending(p => p.Name).ThenBy(p => p.Id),
            _ => projects.OrderBy(p => p.Name).ThenBy(p => p.Id),
        };

        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var total = await projects.CountAsync(cancellationToken);
        var rows = await projects
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                Project = p,
                MemberCount = p.Memberships.Count,
                ExplicitRole = p.Memberships.Where(m => m.UserId == userId).Select(m => (ProjectRole?)m.Role).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => r.Project.ToDto(
                AccessRules.EffectiveProjectRole(organizationAccess.Role, r.ExplicitRole) ?? ProjectRole.Viewer,
                r.MemberCount))
            .ToList();
        return new Api.PagedResult<ProjectDto>(items, page, pageSize, total);
    }

    public async Task<ProjectDto> GetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var projectAccess = await access.RequireProjectAsync(projectId, ProjectPermission.View, cancellationToken);
        return await ToDtoAsync(projectAccess, cancellationToken);
    }

    public async Task<ProjectDto> CreateAsync(Guid organizationId, CreateProjectRequest request, CancellationToken cancellationToken)
    {
        var organizationAccess = await access.RequireOrganizationAsync(organizationId, OrganizationPermission.CreateProjects, cancellationToken);
        var userId = currentUser.RequireUserId();
        var project = Project.Create(organizationId, request.Code, request.Name, request.Description, userId, clock.UtcNow);

        if (await db.Projects.AnyAsync(p => p.OrganizationId == organizationId && p.NormalizedCode == project.NormalizedCode, cancellationToken))
        {
            throw new ConflictException("Проект с таким кодом уже существует в организации.");
        }

        db.Projects.Add(project);
        audit.Record(AuditActions.ProjectCreated, AuditEntityTypes.Project, project.Id.ToString(), organizationId, project.Id,
            new { project.Code, project.Name });
        await db.SaveChangesAsync(cancellationToken);

        var role = AccessRules.EffectiveProjectRole(organizationAccess.Role, null) ?? ProjectRole.ProjectManager;
        return project.ToDto(role, 0);
    }

    public async Task<ProjectDto> UpdateAsync(Guid projectId, UpdateProjectRequest request, CancellationToken cancellationToken)
    {
        var projectAccess = await access.RequireProjectAsync(projectId, ProjectPermission.EditSettings, cancellationToken);
        var project = projectAccess.Project;
        project.Update(request.Name, request.Description, request.Status.ToDomain(), clock.UtcNow);
        audit.Record(AuditActions.ProjectUpdated, AuditEntityTypes.Project, project.Id.ToString(), project.OrganizationId, project.Id,
            new { project.Name, status = project.Status.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(projectAccess, cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectMemberDto>> ListMembersAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await access.RequireProjectAsync(projectId, ProjectPermission.View, cancellationToken);
        var rows = await db.ProjectMemberships.AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.User!.DisplayName)
            .Select(m => new { m.UserId, m.User!.Login, m.User.DisplayName, m.Role, m.CreatedAtUtc })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new ProjectMemberDto(r.UserId, r.Login, r.DisplayName, r.Role.ToApi(), r.CreatedAtUtc)).ToList();
    }

    public async Task<ProjectMemberDto> AddMemberAsync(Guid projectId, AddProjectMemberRequest request, CancellationToken cancellationToken)
    {
        var projectAccess = await access.RequireProjectAsync(projectId, ProjectPermission.ManageMembers, cancellationToken);
        var role = request.Role.ToDomain();
        var normalized = Guard.NormalizeLogin(Guard.Login(request.Login));
        var organizationId = projectAccess.Project.OrganizationId;

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedLogin == normalized, cancellationToken);
        var isOrganizationMember = user is not null && await db.OrganizationMemberships
            .AnyAsync(m => m.OrganizationId == organizationId && m.UserId == user.Id, cancellationToken);
        if (user is null || !isOrganizationMember)
        {
            // Не раскрываем существование пользователей вне организации.
            throw new NotFoundException("Пользователь с таким логином не найден среди участников организации.");
        }

        if (await db.ProjectMemberships.AnyAsync(m => m.ProjectId == projectId && m.UserId == user.Id, cancellationToken))
        {
            throw new ConflictException("Пользователь уже является участником проекта.");
        }

        var membership = ProjectMembership.Create(projectId, user.Id, role, clock.UtcNow);
        db.ProjectMemberships.Add(membership);
        audit.Record(AuditActions.ProjectMemberAdded, AuditEntityTypes.Project, projectId.ToString(), organizationId, projectId,
            new { userId = user.Id, user.Login, role = role.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return new ProjectMemberDto(user.Id, user.Login, user.DisplayName, role.ToApi(), membership.CreatedAtUtc);
    }

    public async Task<ProjectMemberDto> UpdateMemberRoleAsync(Guid projectId, Guid userId, UpdateProjectMemberRequest request, CancellationToken cancellationToken)
    {
        var projectAccess = await access.RequireProjectAsync(projectId, ProjectPermission.ManageMembers, cancellationToken);
        var role = request.Role.ToDomain();
        var membership = await db.ProjectMemberships
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(MemberNotFound);

        var previous = membership.Role;
        membership.ChangeRole(role);
        audit.Record(AuditActions.ProjectMemberRoleChanged, AuditEntityTypes.Project, projectId.ToString(), projectAccess.Project.OrganizationId, projectId,
            new { userId, from = previous.ToString(), to = role.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        var user = membership.User!;
        return new ProjectMemberDto(user.Id, user.Login, user.DisplayName, role.ToApi(), membership.CreatedAtUtc);
    }

    public async Task RemoveMemberAsync(Guid projectId, Guid userId, CancellationToken cancellationToken)
    {
        var projectAccess = await access.RequireProjectAsync(projectId, ProjectPermission.ManageMembers, cancellationToken);
        var membership = await db.ProjectMemberships
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(MemberNotFound);

        db.ProjectMemberships.Remove(membership);
        audit.Record(AuditActions.ProjectMemberRemoved, AuditEntityTypes.Project, projectId.ToString(), projectAccess.Project.OrganizationId, projectId,
            new { userId });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<ProjectDto> ToDtoAsync(ProjectAccess projectAccess, CancellationToken cancellationToken)
    {
        var memberCount = await db.ProjectMemberships.CountAsync(m => m.ProjectId == projectAccess.Project.Id, cancellationToken);
        return projectAccess.Project.ToDto(projectAccess.EffectiveRole, memberCount);
    }
}
