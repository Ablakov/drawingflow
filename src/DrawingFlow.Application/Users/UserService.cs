using DrawingFlow.Application.Abstractions;
using DrawingFlow.Application.Audit;
using DrawingFlow.Application.Authorization;
using DrawingFlow.Application.Common;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Users;
using DrawingFlow.Domain.Authorization;
using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Entities;
using DrawingFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Api = DrawingFlow.Contracts;

namespace DrawingFlow.Application.Users;

public sealed class UserService(
    IAppDbContext db,
    IClock clock,
    IPasswordHasher passwordHasher,
    ICurrentUser currentUser,
    AccessService access,
    AuditService audit)
{
    public async Task<CurrentUserDto> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new UnauthorizedException();
        if (!user.IsActive)
        {
            throw new UnauthorizedException("Учётная запись заблокирована. Обратитесь к администратору.");
        }

        List<OrganizationSummaryDto> organizations;
        if (user.IsServerAdministrator)
        {
            var rows = await db.Organizations.AsNoTracking()
                .OrderBy(o => o.Name)
                .Select(o => new { o.Id, o.Code, o.Name, o.IsActive })
                .ToListAsync(cancellationToken);
            organizations = rows.Select(o => new OrganizationSummaryDto(o.Id, o.Code, o.Name, o.IsActive, Api.OrganizationRole.Owner)).ToList();
        }
        else
        {
            var rows = await db.OrganizationMemberships.AsNoTracking()
                .Where(m => m.UserId == userId)
                .OrderBy(m => m.Organization!.Name)
                .Select(m => new { m.OrganizationId, m.Organization!.Code, m.Organization.Name, m.Organization.IsActive, m.Role })
                .ToListAsync(cancellationToken);
            organizations = rows.Select(o => new OrganizationSummaryDto(o.OrganizationId, o.Code, o.Name, o.IsActive, o.Role.ToApi())).ToList();
        }

        return new CurrentUserDto(user.ToDto(), organizations);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        OrganizationAccess? organizationAccess = null;
        if (request.OrganizationId is Guid organizationId)
        {
            organizationAccess = await access.RequireOrganizationAsync(organizationId, OrganizationPermission.CreateUsers, cancellationToken);
        }
        else if (!currentUser.IsServerAdministrator)
        {
            throw new ForbiddenException("Создавать пользователей вне организации может только администратор сервера.");
        }

        if (request.IsServerAdministrator && !currentUser.IsServerAdministrator)
        {
            throw new ForbiddenException("Назначать администраторов сервера может только администратор сервера.");
        }

        Guard.Password(request.Password);
        var now = clock.UtcNow;
        var user = User.Create(request.Login, request.DisplayName, request.Email, passwordHasher.Hash(request.Password!), request.IsServerAdministrator, now, request.RequirePasswordChange);

        if (await db.Users.AnyAsync(u => u.NormalizedLogin == user.NormalizedLogin, cancellationToken))
        {
            throw new ConflictException("Пользователь с таким логином уже существует.");
        }

        db.Users.Add(user);
        audit.Record(AuditActions.UserCreated, AuditEntityTypes.User, user.Id.ToString(), organizationAccess?.Organization.Id,
            details: new { user.Login, user.IsServerAdministrator });

        if (organizationAccess is not null)
        {
            var role = (request.OrganizationRole ?? Api.OrganizationRole.Member).ToDomain("organizationRole");
            if (!AccessRules.CanAssignOrganizationRole(organizationAccess.Role, role))
            {
                throw new ForbiddenException("Недостаточно прав для назначения этой роли.");
            }

            db.OrganizationMemberships.Add(OrganizationMembership.Create(organizationAccess.Organization.Id, user.Id, role, now));
            audit.Record(AuditActions.OrganizationMemberAdded, AuditEntityTypes.Organization, organizationAccess.Organization.Id.ToString(),
                organizationAccess.Organization.Id, details: new { userId = user.Id, user.Login, role = role.ToString() });
        }

        await db.SaveChangesAsync(cancellationToken);
        return user.ToDto();
    }

    public async Task<PagedResult<UserDto>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        currentUser.RequireServerAdministrator();
        var (normalizedPage, normalizedPageSize) = Paging.Normalize(page, pageSize);

        IQueryable<User> users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            users = users.Where(u => u.Login.ToLower().Contains(term) || u.DisplayName.ToLower().Contains(term));
        }

        var total = await users.CountAsync(cancellationToken);
        var items = await users
            .OrderBy(u => u.DisplayName).ThenBy(u => u.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<UserDto>(items.Select(u => u.ToDto()).ToList(), normalizedPage, normalizedPageSize, total);
    }

    public async Task<UserDto> UpdateAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        currentUser.RequireServerAdministrator();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("Пользователь не найден.");

        user.UpdateProfile(request.DisplayName, request.Email);
        if (user.IsActive != request.IsActive)
        {
            if (!request.IsActive && user.Id == currentUser.UserId)
            {
                throw new DomainRuleException("Нельзя заблокировать собственную учётную запись.");
            }

            if (request.IsActive)
            {
                user.Activate();
                audit.Record(AuditActions.UserActivated, AuditEntityTypes.User, user.Id.ToString());
            }
            else
            {
                user.Deactivate();
                var now = clock.UtcNow;
                var tokens = await db.RefreshTokens
                    .Where(t => t.UserId == userId && t.RevokedAtUtc == null && t.ExpiresAtUtc > now)
                    .ToListAsync(cancellationToken);
                foreach (var token in tokens)
                {
                    token.Revoke(now);
                }

                audit.Record(AuditActions.UserDeactivated, AuditEntityTypes.User, user.Id.ToString());
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return user.ToDto();
    }
}
