using System.Text.Json;
using DrawingFlow.Application.Abstractions;
using DrawingFlow.Application.Audit;
using DrawingFlow.Application.Authorization;
using DrawingFlow.Application.Common;
using DrawingFlow.Contracts.Agents;
using DrawingFlow.Domain.Authorization;
using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Entities;
using DrawingFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Dom = DrawingFlow.Domain.Enums;

namespace DrawingFlow.Application.Agents;

/// <summary>Регистрация и жизненный цикл агентов, конфигурация и отчёты агента.</summary>
public sealed class AgentService(
    IAppDbContext db,
    IClock clock,
    ICurrentUser currentUser,
    ITokenService tokenService,
    AccessService access,
    AuditService audit,
    ILogger<AgentService> logger)
{
    public const string AgentNotFound = "Агент не найден.";
    public const string InvalidAgentKey = "Ключ агента недействителен или отозван.";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------- Администрирование (пользователи) ----------

    public async Task<IReadOnlyList<AgentDto>> ListAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        await access.RequireOrganizationAsync(organizationId, OrganizationPermission.ManageAgents, cancellationToken);
        var rows = await db.AgentInstallations.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId)
            .OrderBy(a => a.RevokedAtUtc != null).ThenBy(a => a.Name)
            .Select(a => new { Agent = a, FolderCount = a.Folders.Count })
            .ToListAsync(cancellationToken);
        var names = await UserNamesAsync(rows.Select(r => r.Agent.CreatedByUserId), cancellationToken);
        var now = clock.UtcNow;
        return rows.Select(r => ToDto(r.Agent, r.FolderCount, names, now)).ToList();
    }

    public async Task<AgentRegistrationResultDto> RegisterAsync(Guid organizationId, RegisterAgentRequest request, CancellationToken cancellationToken)
    {
        await access.RequireOrganizationAsync(organizationId, OrganizationPermission.ManageAgents, cancellationToken);
        var userId = currentUser.RequireUserId();
        var key = tokenService.CreateAgentKeyValue();
        var agent = AgentInstallation.Create(organizationId, request.Name, tokenService.HashAgentKey(key), key[..AgentInstallation.CredentialPrefixLength], userId, clock.UtcNow);
        db.AgentInstallations.Add(agent);
        audit.Record(AuditActions.AgentRegistered, AuditEntityTypes.Agent, agent.Id.ToString(), organizationId, details: new { agent.Name, agent.CredentialPrefix });
        await db.SaveChangesAsync(cancellationToken);

        var names = await UserNamesAsync([userId], cancellationToken);
        return new AgentRegistrationResultDto(ToDto(agent, 0, names, clock.UtcNow), key);
    }

    public async Task<AgentDto> RevokeAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var agent = await db.AgentInstallations.FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken)
            ?? throw new NotFoundException(AgentNotFound);
        await access.RequireOrganizationAsync(agent.OrganizationId, OrganizationPermission.ManageAgents, cancellationToken);
        var userId = currentUser.RequireUserId();
        var now = clock.UtcNow;
        agent.Revoke(userId, now);

        var folders = await db.WatchedFolders.Where(f => f.AgentInstallationId == agentId && f.IsEnabled).ToListAsync(cancellationToken);
        foreach (var folder in folders)
        {
            folder.Disable(now);
        }

        audit.Record(AuditActions.AgentRevoked, AuditEntityTypes.Agent, agent.Id.ToString(), agent.OrganizationId, details: new { agent.Name, disabledFolders = folders.Count });
        await db.SaveChangesAsync(cancellationToken);

        var folderCount = await db.WatchedFolders.CountAsync(f => f.AgentInstallationId == agentId, cancellationToken);
        return ToDto(agent, folderCount, await UserNamesAsync([agent.CreatedByUserId], cancellationToken), now);
    }

    public async Task<SyncSummaryDto> GetSummaryAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        await access.RequireOrganizationAsync(organizationId, OrganizationPermission.View, cancellationToken);
        var now = clock.UtcNow;
        var agents = await db.AgentInstallations.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId)
            .Select(a => new { a.RevokedAtUtc, a.LastHeartbeatAtUtc })
            .ToListAsync(cancellationToken);
        var onlineSince = now - AgentInstallation.OnlineWindow;

        var statuses = await db.SyncOperations.AsNoTracking()
            .Where(o => o.OrganizationId == organizationId && o.Status != Dom.SyncOperationStatus.Completed && o.Status != Dom.SyncOperationStatus.Dismissed && o.Status != Dom.SyncOperationStatus.Cancelled)
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return new SyncSummaryDto(
            agents.Count,
            agents.Count(a => a.RevokedAtUtc == null && a.LastHeartbeatAtUtc >= onlineSince),
            agents.Count(a => a.RevokedAtUtc != null),
            statuses.Where(s => s.Status is Dom.SyncOperationStatus.Pending or Dom.SyncOperationStatus.Processing).Sum(s => s.Count),
            statuses.Where(s => s.Status == Dom.SyncOperationStatus.Failed).Sum(s => s.Count),
            statuses.Where(s => s.Status == Dom.SyncOperationStatus.NeedsResolution).Sum(s => s.Count),
            agents.Max(a => a.LastHeartbeatAtUtc));
    }

    // ---------- Аутентификация агента ----------

    /// <summary>Обмен ключа установки на короткоживущий токен. Анонимно, с ограничением частоты.</summary>
    public async Task<AgentTokenResponse> ExchangeKeyAsync(AgentTokenRequest request, CancellationToken cancellationToken)
    {
        var key = request.AgentKey?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            throw new UnauthorizedException(InvalidAgentKey);
        }

        var hash = tokenService.HashAgentKey(key);
        var agent = await db.AgentInstallations.FirstOrDefaultAsync(a => a.CredentialHash == hash, cancellationToken);
        if (agent is null || agent.IsRevoked)
        {
            logger.LogWarning("Отклонён ключ агента с префиксом {Prefix}", key.Length >= 8 ? key[..8] : key);
            throw new UnauthorizedException(InvalidAgentKey);
        }

        agent.Touch(clock.UtcNow, request.MachineName, request.AgentVersion);
        await db.SaveChangesAsync(cancellationToken);
        var token = tokenService.CreateAgentAccessToken(agent);
        return new AgentTokenResponse(token.Value, token.ExpiresAtUtc, agent.Id, agent.Name, agent.OrganizationId);
    }

    // ---------- Операции от имени агента ----------

    public async Task<AgentConfigurationDto> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        var agent = await RequireActiveAgentAsync(cancellationToken);
        var folders = await db.WatchedFolders.AsNoTracking()
            .Where(f => f.AgentInstallationId == agent.Id)
            .OrderBy(f => f.Path)
            .Select(f => new { Folder = f, f.Project!.Code, f.Project.Name })
            .ToListAsync(cancellationToken);
        var organizationName = await db.Organizations.AsNoTracking().Where(o => o.Id == agent.OrganizationId).Select(o => o.Name).FirstAsync(cancellationToken);
        return new AgentConfigurationDto(
            agent.Id,
            agent.Name,
            agent.OrganizationId,
            organizationName,
            clock.UtcNow,
            folders.Select(f => WatchedFolderService.ToDto(f.Folder, f.Code, f.Name)).ToList());
    }

    public async Task HeartbeatAsync(AgentHeartbeatRequest request, CancellationToken cancellationToken)
    {
        var agent = await RequireActiveAgentAsync(cancellationToken);
        agent.RecordHeartbeat(clock.UtcNow, request.MachineName, request.AgentVersion, request.PendingOperations, request.FailedOperations, request.UnresolvedOperations);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Принимает отчёты агента об операциях (создание или обновление) и возвращает запрошенные пользователями действия.</summary>
    public async Task<IReadOnlyList<AgentActionDto>> ReportOperationsAsync(IReadOnlyList<ReportSyncOperationRequest> reports, CancellationToken cancellationToken)
    {
        var agent = await RequireActiveAgentAsync(cancellationToken);
        if (reports.Count == 0)
        {
            return await GetActionsAsync(cancellationToken);
        }

        if (reports.Count > 200)
        {
            throw new ValidationException("reports", "За один запрос можно передать не более 200 операций.");
        }

        var ids = reports.Select(r => r.OperationId).Distinct().ToList();
        var existing = await db.SyncOperations.Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        var allowedProjects = await db.WatchedFolders.AsNoTracking()
            .Where(f => f.AgentInstallationId == agent.Id)
            .Select(f => f.ProjectId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var now = clock.UtcNow;

        foreach (var report in reports)
        {
            if (!allowedProjects.Contains(report.ProjectId))
            {
                throw new NotFoundException(AccessService.ProjectNotFound);
            }

            var candidates = report.Candidates is { Count: > 0 } ? JsonSerializer.Serialize(report.Candidates.Take(20), Json) : null;
            if (existing.TryGetValue(report.OperationId, out var operation))
            {
                if (operation.AgentInstallationId != agent.Id)
                {
                    throw new NotFoundException("Операция не найдена.");
                }
            }
            else
            {
                operation = SyncOperation.Create(report.OperationId, agent.Id, agent.OrganizationId, report.WatchedFolderId, report.ProjectId,
                    report.FilePath, report.FileName, Paging.ToUtc(report.DetectedAtUtc) ?? now, now);
                db.SyncOperations.Add(operation);
                existing[operation.Id] = operation;
            }

            operation.ApplyReport(ToDomain(report.Status), report.FileSize, report.Sha256Hash, report.RetryCount, report.LastError, report.DocumentId, report.DocumentVersionId, candidates, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetActionsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentActionDto>> GetActionsAsync(CancellationToken cancellationToken)
    {
        var agentId = currentUser.RequireAgentId();
        var actions = await db.SyncOperations.AsNoTracking()
            .Where(o => o.AgentInstallationId == agentId && o.RequestedAction != Dom.SyncRequestedAction.None)
            .OrderBy(o => o.RequestedAtUtc)
            .Select(o => new { o.Id, o.RequestedAction, o.RequestedDocumentId })
            .ToListAsync(cancellationToken);
        return actions.Select(a => new AgentActionDto(a.Id, (SyncRequestedAction)(int)a.RequestedAction, a.RequestedDocumentId)).ToList();
    }

    // ---------- Помощники ----------

    private async Task<AgentInstallation> RequireActiveAgentAsync(CancellationToken cancellationToken)
    {
        var agentId = currentUser.RequireAgentId();
        var agent = await db.AgentInstallations.FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken);
        if (agent is null || agent.IsRevoked)
        {
            throw new UnauthorizedException(InvalidAgentKey);
        }

        return agent;
    }

    private async Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
    }

    public static AgentDto ToDto(AgentInstallation agent, int folderCount, IReadOnlyDictionary<Guid, string> names, DateTime nowUtc) => new(
        agent.Id,
        agent.OrganizationId,
        agent.Name,
        agent.MachineName,
        agent.AgentVersion,
        agent.CredentialPrefix,
        agent.IsRevoked,
        agent.IsOnline(nowUtc),
        agent.CreatedAtUtc,
        names.GetValueOrDefault(agent.CreatedByUserId, string.Empty),
        agent.RevokedAtUtc,
        agent.LastSeenAtUtc,
        agent.LastHeartbeatAtUtc,
        agent.PendingOperations,
        agent.FailedOperations,
        agent.UnresolvedOperations,
        folderCount);

    private static Dom.SyncOperationStatus ToDomain(SyncOperationStatus status)
    {
        var value = (Dom.SyncOperationStatus)(int)status;
        return Enum.IsDefined(value) ? value : throw new ValidationException("status", "Недопустимый статус операции.");
    }
}
