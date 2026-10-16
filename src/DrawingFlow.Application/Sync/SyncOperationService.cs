using System.Text.Json;
using DrawingFlow.Application.Abstractions;
using DrawingFlow.Application.Audit;
using DrawingFlow.Application.Authorization;
using DrawingFlow.Application.Common;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Agents;
using DrawingFlow.Domain.Authorization;
using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Entities;
using DrawingFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Dom = DrawingFlow.Domain.Enums;

namespace DrawingFlow.Application.Sync;

/// <summary>Просмотр очереди синхронизации и действия пользователя над операциями (повтор, сопоставление, отклонение).</summary>
public sealed class SyncOperationService(
    IAppDbContext db,
    IClock clock,
    ICurrentUser currentUser,
    AccessService access,
    AuditService audit)
{
    public const string OperationNotFound = "Операция синхронизации не найдена.";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PagedResult<SyncOperationDto>> ListAsync(Guid organizationId, SyncOperationListQuery query, CancellationToken cancellationToken)
    {
        var organizationAccess = await access.RequireOrganizationAsync(organizationId, OrganizationPermission.View, cancellationToken);
        IQueryable<SyncOperation> operations = db.SyncOperations.AsNoTracking().Where(o => o.OrganizationId == organizationId);

        if (!organizationAccess.Permissions.Has(OrganizationPermission.ManageAgents))
        {
            // Инженеры и руководители видят операции только по своим проектам.
            var userId = currentUser.RequireUserId();
            var visible = await db.ProjectMemberships.AsNoTracking()
                .Where(m => m.UserId == userId && m.Project!.OrganizationId == organizationId && m.Role != Dom.ProjectRole.Viewer)
                .Select(m => m.ProjectId)
                .ToListAsync(cancellationToken);
            operations = operations.Where(o => visible.Contains(o.ProjectId));
        }

        if (query.AgentId is not null)
        {
            operations = operations.Where(o => o.AgentInstallationId == query.AgentId);
        }

        if (query.ProjectId is not null)
        {
            operations = operations.Where(o => o.ProjectId == query.ProjectId);
        }

        if (query.Status is not null)
        {
            var status = (Dom.SyncOperationStatus)(int)query.Status.Value;
            operations = operations.Where(o => o.Status == status);
        }

        if (query.ActiveOnly)
        {
            operations = operations.Where(o => o.Status != Dom.SyncOperationStatus.Completed && o.Status != Dom.SyncOperationStatus.Dismissed && o.Status != Dom.SyncOperationStatus.Cancelled);
        }

        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var total = await operations.CountAsync(cancellationToken);
        var rows = await operations
            .OrderByDescending(o => o.UpdatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new Row(o, o.Agent!.Name, o.WatchedFolder != null ? o.WatchedFolder.Path : null, o.Project!.Code))
            .ToListAsync(cancellationToken);
        return new PagedResult<SyncOperationDto>(await MapAsync(rows, cancellationToken), page, pageSize, total);
    }

    public async Task<SyncOperationDto> ResolveAsync(Guid operationId, ResolveSyncOperationRequest request, CancellationToken cancellationToken)
    {
        var operation = await LoadAsync(operationId, cancellationToken);
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.ProjectId == operation.ProjectId, cancellationToken)
            ?? throw new NotFoundException("Документ не найден в проекте операции.");
        if (document.Status == Dom.DocumentStatus.Archived)
        {
            throw new DomainRuleException("Документ в архиве: сначала восстановите его.");
        }

        operation.RequestUpload(document.Id, currentUser.RequireUserId(), clock.UtcNow);
        audit.Record(AuditActions.SyncOperationResolved, AuditEntityTypes.SyncOperation, operation.Id.ToString(), operation.OrganizationId, operation.ProjectId,
            new { operation.FileName, documentId = document.Id, document.DocumentNumber });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(operationId, cancellationToken);
    }

    public async Task<SyncOperationDto> RetryAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var operation = await LoadAsync(operationId, cancellationToken);
        operation.RequestRetry(currentUser.RequireUserId(), clock.UtcNow);
        audit.Record(AuditActions.SyncOperationRetried, AuditEntityTypes.SyncOperation, operation.Id.ToString(), operation.OrganizationId, operation.ProjectId, new { operation.FileName });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(operationId, cancellationToken);
    }

    public async Task<SyncOperationDto> DismissAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var operation = await LoadAsync(operationId, cancellationToken);
        operation.Dismiss(currentUser.RequireUserId(), clock.UtcNow);
        audit.Record(AuditActions.SyncOperationDismissed, AuditEntityTypes.SyncOperation, operation.Id.ToString(), operation.OrganizationId, operation.ProjectId, new { operation.FileName });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(operationId, cancellationToken);
    }

    private async Task<SyncOperationDto> GetAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var row = await db.SyncOperations.AsNoTracking()
            .Where(o => o.Id == operationId)
            .Select(o => new Row(o, o.Agent!.Name, o.WatchedFolder != null ? o.WatchedFolder.Path : null, o.Project!.Code))
            .FirstAsync(cancellationToken);
        return (await MapAsync([row], cancellationToken))[0];
    }

    private async Task<SyncOperation> LoadAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var operation = await db.SyncOperations.FirstOrDefaultAsync(o => o.Id == operationId, cancellationToken)
            ?? throw new NotFoundException(OperationNotFound);
        await access.RequireProjectAsync(operation.ProjectId, ProjectPermission.UploadVersions, cancellationToken);
        return operation;
    }

    private sealed record Row(SyncOperation Operation, string AgentName, string? FolderPath, string ProjectCode);

    private async Task<List<SyncOperationDto>> MapAsync(IReadOnlyList<Row> rows, CancellationToken cancellationToken)
    {
        var documentIds = rows.Where(r => r.Operation.DocumentId.HasValue).Select(r => r.Operation.DocumentId!.Value).Distinct().ToList();
        var documents = documentIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Documents.AsNoTracking().Where(d => documentIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.DocumentNumber, cancellationToken);
        var versionIds = rows.Where(r => r.Operation.DocumentVersionId.HasValue).Select(r => r.Operation.DocumentVersionId!.Value).Distinct().ToList();
        var versions = versionIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await db.DocumentVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.VersionNumber, cancellationToken);

        return rows.Select(r =>
        {
            var o = r.Operation;
            IReadOnlyList<SyncCandidateDto> candidates = [];
            if (!string.IsNullOrEmpty(o.Candidates))
            {
                try
                {
                    candidates = JsonSerializer.Deserialize<List<SyncCandidateDto>>(o.Candidates, Json) ?? [];
                }
                catch (JsonException)
                {
                    candidates = [];
                }
            }

            return new SyncOperationDto(
                o.Id, o.AgentInstallationId, r.AgentName, o.WatchedFolderId, r.FolderPath, o.ProjectId, r.ProjectCode,
                o.FilePath, o.FileName, o.FileSize, o.Sha256Hash,
                (SyncOperationStatus)(int)o.Status, o.DetectedAtUtc, o.UpdatedAtUtc, o.CompletedAtUtc, o.RetryCount, o.LastError,
                o.DocumentId, o.DocumentId is Guid d ? documents.GetValueOrDefault(d) : null,
                o.DocumentVersionId, o.DocumentVersionId is Guid v && versions.TryGetValue(v, out var number) ? number : null,
                candidates, (SyncRequestedAction)(int)o.RequestedAction, o.RequestedDocumentId);
        }).ToList();
    }
}
