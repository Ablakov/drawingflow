using DrawingFlow.Application.Abstractions;
using DrawingFlow.Application.Audit;
using DrawingFlow.Application.Authorization;
using DrawingFlow.Application.Common;
using DrawingFlow.Contracts.Organizations;
using DrawingFlow.Domain.Authorization;
using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Entities;
using DrawingFlow.Domain.Enums;
using DrawingFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DrawingFlow.Application.Organizations;

public sealed class OrganizationService(
    IAppDbContext db,
    IClock clock,
    ICurrentUser currentUser,
    AccessService access,
    AuditService audit)
{
    private const string UserNotFound = "Пользователь с таким логином не найден.";
    private const string MemberNotFound = "Участник не найден в организации.";
    private const string LastOwner = "В организации должен оставаться хотя бы один владелец.";
    private const string RoleNotAllowed = "Недостаточно прав для назначения или изменения этой роли.";

    public async Task<IReadOnlyList<OrganizationDto>> ListForCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        if (currentUser.IsServerAdministrator)
        {
            var all = await db.Organizations.AsNoTracking()
                .OrderBy(o => o.Name)
                .Select(o => new { Organization = o, ProjectCount = o.Projects.Count, MemberCount = o.Memberships.Count })
                .ToListAsync(cancellationToken);
            return all.Select(x => x.Organization.ToDto(OrganizationRole.Owner, x.ProjectCount, x.MemberCount)).ToList();
        }

        var rows = await db.OrganizationMemberships.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Organization!.Name)
            .Select(m => new
            {
                Organization = m.Organization!,
                m.Role,
                ProjectCount = m.Organization!.Projects.Count,
                MemberCount = m.Organization.Memberships.Count,
            })
            .ToListAsync(cancellationToken);
        return rows.Select(x => x.Organization.ToDto(x.Role, x.ProjectCount, x.MemberCount)).ToList();
    }

    public async Task<OrganizationDto> GetAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var organizationAccess = await access.RequireOrganizationAsync(organizationId, OrganizationPermission.View, cancellationToken);
        return await ToDtoAsync(organizationAccess, cancellationToken);
    }

    /// <summary>Создание организации доступно администратору сервера. Владельцем становится указанный пользователь или создатель.</summary>
    public async Task<OrganizationDto> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        currentUser.RequireServerAdministrator();
        var creatorId = currentUser.RequireUserId();
        var now = clock.UtcNow;
        var organization = Organization.Create(request.Code, request.Name, creatorId, now);

        if (await db.Organizations.AnyAsync(o => o.NormalizedCode == organization.NormalizedCode, cancellationToken))
        {
            throw new ConflictException("Организация с таким кодом уже существует.");
        }

        var ownerId = creatorId;
        if (!string.IsNullOrWhiteSpace(request.OwnerLogin))
        {
            var normalized = Guard.NormalizeLogin(request.OwnerLogin);
            ownerId = await db.Users.Where(u => u.NormalizedLogin == normalized).Select(u => u.Id).FirstOrDefaultAsync(cancellationToken);
            if (ownerId == Guid.Empty)
            {
                throw new NotFoundException(UserNotFound);
            }
        }

        db.Organizations.Add(organization);
        db.OrganizationMemberships.Add(OrganizationMembership.Create(organization.Id, ownerId, OrganizationRole.Owner, now));
        audit.Record(AuditActions.OrganizationCreated, AuditEntityTypes.Organization, organization.Id.ToString(), organization.Id,
            details: new { organization.Code, organization.Name, ownerUserId = ownerId });
        await db.SaveChangesAsync(cancellationToken);

        var role = currentUser.IsServerAdministrator ? OrganizationRole.Owner : OrganizationRole.Owner;
        return organization.ToDto(role, 0, 1);
    }

    public async Task<OrganizationDto> UpdateAsync(Guid organizationId, UpdateOrganizationRequest request, CancellationToken cancellationToken)
    {
        var organizationAccess = await access.RequireOrganizationAsync(organizationId, OrganizationPermission.ManageSettings, cancellationToken);
        var organization = organizationAccess.Organization;
        organization.Rename(request.Name);
        organization.SetActive(request.IsActive);
        audit.Record(AuditActions.OrganizationUpdated, AuditEntityTypes.Organization, organization.Id.ToString(), organization.Id,
            details: new { organization.Name, organization.IsActive });
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(organizationAccess, cancellationToken);
    }

    public async Task<IReadOnlyList<OrganizationMemberDto>> ListMembersAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        await access.RequireOrganizationAsync(organizationId, OrganizationPermission.View, cancellationToken);
        var rows = await db.OrganizationMemberships.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId)
            .OrderBy(m => m.User!.DisplayName)
            .Select(m => new
            {
                m.UserId,
                m.User!.Login,
                m.User.DisplayName,
                m.User.Email,
                m.User.IsActive,
                m.Role,
                m.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new OrganizationMemberDto(r.UserId, r.Login, r.DisplayName, r.Email, r.IsActive, r.Role.ToApi(), r.CreatedAtUtc)).ToList();
    }

    public async Task<OrganizationMemberDto> AddMemberAsync(Guid organizationId, AddOrganizationMemberRequest request, CancellationToken cancellationToken)
    {
        var organizationAccess = await access.RequireOrganizationAsync(organizationId, OrganizationPermission.ManageMembers, cancellationToken);
        var role = request.Role.ToDomain();
        if (!AccessRules.CanAssignOrganizationRole(organizationAccess.Role, role))
        {
            throw new ForbiddenException(RoleNotAllowed);
        }

        var normalized = Guard.NormalizeLogin(Guard.Login(request.Login));
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedLogin == normalized, cancellationToken)
            ?? throw new NotFoundException(UserNotFound);

        if (await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organizationId && m.UserId == user.Id, cancellationToken))
        {
            throw new ConflictException("Пользователь уже является участником организации.");
        }

        var membership = OrganizationMembership.Create(organizationId, user.Id, role, clock.UtcNow);
        db.OrganizationMemberships.Add(membership);
        audit.Record(AuditActions.OrganizationMemberAdded, AuditEntityTypes.Organization, organizationId.ToString(), organizationId,
            details: new { userId = user.Id, user.Login, role = role.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return new OrganizationMemberDto(user.Id, user.Login, user.DisplayName, user.Email, user.IsActive, role.ToApi(), membership.CreatedAtUtc);
    }

    public async Task<OrganizationMemberDto> UpdateMemberRoleAsync(Guid organizationId, Guid userId, UpdateOrganizationMemberRequest request, CancellationToken cancellationToken)
    {
        var organizationAccess = await access.RequireOrganizationAsync(organizationId, OrganizationPermission.ManageMembers, cancellationToken);
        var newRole = request.Role.ToDomain();
        var membership = await db.OrganizationMemberships
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(MemberNotFound);

        if (!AccessRules.CanAssignOrganizationRole(organizationAccess.Role, membership.Role)
            || !AccessRules.CanAssignOrganizationRole(organizationAccess.Role, newRole))
        {
            throw new ForbiddenException(RoleNotAllowed);
        }

        if (membership.Role == OrganizationRole.Owner && newRole != OrganizationRole.Owner)
        {
            await EnsureAnotherOwnerExistsAsync(organizationId, userId, cancellationToken);
        }

        var previousRole = membership.Role;
        membership.ChangeRole(newRole);
        audit.Record(AuditActions.OrganizationMemberRoleChanged, AuditEntityTypes.Organization, organizationId.ToString(), organizationId,
            details: new { userId, from = previousRole.ToString(), to = newRole.ToString() });
        await db.SaveChangesAsync(cancellationToken);

        var user = membership.User!;
        return new OrganizationMemberDto(user.Id, user.Login, user.DisplayName, user.Email, user.IsActive, newRole.ToApi(), membership.CreatedAtUtc);
    }

    /// <summary>Удаляет участника из организации вместе с его ролями во всех её проектах.</summary>
    public async Task RemoveMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        var organizationAccess = await access.RequireOrganizationAsync(organizationId, OrganizationPermission.ManageMembers, cancellationToken);
        var membership = await db.OrganizationMemberships
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(MemberNotFound);

        if (!AccessRules.CanAssignOrganizationRole(organizationAccess.Role, membership.Role))
        {
            throw new ForbiddenException(RoleNotAllowed);
        }

        if (membership.Role == OrganizationRole.Owner)
        {
            await EnsureAnotherOwnerExistsAsync(organizationId, userId, cancellationToken);
        }

        var projectMemberships = await db.ProjectMemberships
            .Where(m => m.UserId == userId && m.Project!.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);
        db.ProjectMemberships.RemoveRange(projectMemberships);
        db.OrganizationMemberships.Remove(membership);
        audit.Record(AuditActions.OrganizationMemberRemoved, AuditEntityTypes.Organization, organizationId.ToString(), organizationId,
            details: new { userId, removedProjectMemberships = projectMemberships.Count });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureAnotherOwnerExistsAsync(Guid organizationId, Guid exceptUserId, CancellationToken cancellationToken)
    {
        var hasAnotherOwner = await db.OrganizationMemberships.AnyAsync(
            m => m.OrganizationId == organizationId && m.UserId != exceptUserId && m.Role == OrganizationRole.Owner,
            cancellationToken);
        if (!hasAnotherOwner)
        {
            throw new DomainRuleException(LastOwner);
        }
    }

    private async Task<OrganizationDto> ToDtoAsync(OrganizationAccess organizationAccess, CancellationToken cancellationToken)
    {
        var organizationId = organizationAccess.Organization.Id;
        var projectCount = await db.Projects.CountAsync(p => p.OrganizationId == organizationId, cancellationToken);
        var memberCount = await db.OrganizationMemberships.CountAsync(m => m.OrganizationId == organizationId, cancellationToken);
        return organizationAccess.Organization.ToDto(organizationAccess.Role, projectCount, memberCount);
    }
}
