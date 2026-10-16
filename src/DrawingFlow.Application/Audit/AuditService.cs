using System.Text.Json;
using DrawingFlow.Application.Abstractions;
using DrawingFlow.Application.Authorization;
using DrawingFlow.Application.Common;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Audit;
using DrawingFlow.Domain.Authorization;
using DrawingFlow.Domain.Entities;
using DrawingFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DrawingFlow.Application.Audit;

/// <summary>
/// Журнал аудита. Запись добавляется в контекст и сохраняется вместе с основной операцией (одна транзакция).
/// </summary>
public sealed class AuditService(IAppDbContext db, IClock clock, ICurrentUser currentUser, AccessService access)
{
    private static readonly JsonSerializerOptions DetailsJsonOptions = new(JsonSerializerDefaults.Web);

    public void Record(string action, string entityType, string? entityId, Guid? organizationId = null, Guid? projectId = null, object? details = null)
    {
        var actorName = currentUser.DisplayName ?? currentUser.Login ?? "system";
        if (currentUser.IsAgent)
        {
            actorName = "Агент: " + actorName;
        }

        RecordAs(actorName, currentUser.UserId, action, entityType, entityId, organizationId, projectId, details, currentUser.AgentId);
    }

    public void RecordAs(string actorName, Guid? actorUserId, string action, string entityType, string? entityId, Guid? organizationId = null, Guid? projectId = null, object? details = null, Guid? actorAgentId = null)
    {
        var json = details is null ? null : JsonSerializer.Serialize(details, DetailsJsonOptions);
        db.AuditEvents.Add(AuditEvent.Create(
            clock.UtcNow,
            action,
            entityType,
            entityId,
            string.IsNullOrWhiteSpace(actorName) ? "system" : actorName,
            actorUserId,
            actorAgentId,
            organizationId,
            projectId,
            json,
            currentUser.ClientAddress));
    }

    public async Task<PagedResult<AuditEventDto>> ListAsync(Guid organizationId, AuditListQuery query, CancellationToken cancellationToken)
    {
        if (query.ProjectId is Guid projectId)
        {
            var projectAccess = await access.RequireProjectAsync(projectId, ProjectPermission.ViewAudit, cancellationToken);
            if (projectAccess.Project.OrganizationId != organizationId)
            {
                throw new NotFoundException(AccessService.ProjectNotFound);
            }
        }
        else
        {
            await access.RequireOrganizationAsync(organizationId, OrganizationPermission.ViewAudit, cancellationToken);
        }

        var events = db.AuditEvents.AsNoTracking().Where(e => e.OrganizationId == organizationId);
        if (query.ProjectId is not null)
        {
            events = events.Where(e => e.ProjectId == query.ProjectId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            var action = query.Action.Trim();
            events = events.Where(e => e.Action == action);
        }

        var fromUtc = Paging.ToUtc(query.FromUtc);
        if (fromUtc is not null)
        {
            events = events.Where(e => e.OccurredAtUtc >= fromUtc);
        }

        var toUtc = Paging.ToUtc(query.ToUtc);
        if (toUtc is not null)
        {
            events = events.Where(e => e.OccurredAtUtc <= toUtc);
        }

        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var total = await events.CountAsync(cancellationToken);
        var items = await events
            .OrderByDescending(e => e.OccurredAtUtc)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new AuditEventDto(
                e.Id,
                e.OccurredAtUtc,
                e.ActorName,
                e.ActorUserId,
                e.ActorAgentId,
                e.Action,
                e.EntityType,
                e.EntityId,
                e.OrganizationId,
                e.ProjectId,
                e.Details))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditEventDto>(items, page, pageSize, total);
    }
}
