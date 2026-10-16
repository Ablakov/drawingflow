using DrawingFlow.Application.Abstractions;
using DrawingFlow.Application.Audit;
using DrawingFlow.Application.Authorization;
using DrawingFlow.Application.Common;
using DrawingFlow.Application.Storage;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Documents;
using DrawingFlow.Domain.Authorization;
using DrawingFlow.Domain.Common;
using DrawingFlow.Domain.Entities;
using DrawingFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Dom = DrawingFlow.Domain.Enums;

namespace DrawingFlow.Application.Documents;

public sealed record UploadVersionCommand(
    string FileName,
    string? ContentType,
    string? Comment,
    string IdempotencyKey,
    string? ExpectedSha256Hash,
    Stream Content);

public sealed record UploadVersionResult(DocumentVersionDto Version, bool Created);

public sealed record VersionContent(DocumentVersionDto Version, Stream Content);

public sealed class DocumentService(
    IAppDbContext db,
    IClock clock,
    ICurrentUser currentUser,
    AccessService access,
    AuditService audit,
    IFileStorage storage,
    ILogger<DocumentService> logger)
{
    public const string DocumentNotFound = "Документ не найден.";
    public const string VersionNotFound = "Версия документа не найдена.";
    private const int MaxRecent = 50;

    // ---------- Чтение ----------

    public async Task<PagedResult<DocumentDto>> ListAsync(Guid projectId, DocumentListQuery query, CancellationToken cancellationToken)
    {
        var projectAccess = await access.RequireProjectAsync(projectId, ProjectPermission.View, cancellationToken);
        IQueryable<Document> documents = db.Documents.AsNoTracking().Where(d => d.ProjectId == projectId);

        if (query.Status is not null)
        {
            var status = ToDomain(query.Status.Value);
            documents = documents.Where(d => d.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Discipline))
        {
            var discipline = query.Discipline.Trim().ToLower();
            documents = documents.Where(d => d.Discipline != null && d.Discipline.ToLower() == discipline);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var upper = Guard.NormalizeDocumentNumber(query.Search);
            var lower = query.Search.Trim().ToLower();
            documents = documents.Where(d => d.NormalizedDocumentNumber.Contains(upper) || d.Title.ToLower().Contains(lower));
        }

        documents = (query.Sort, query.Descending) switch
        {
            (DocumentSortField.Title, false) => documents.OrderBy(d => d.Title).ThenBy(d => d.Id),
            (DocumentSortField.Title, true) => documents.OrderByDescending(d => d.Title).ThenBy(d => d.Id),
            (DocumentSortField.UpdatedAt, false) => documents.OrderBy(d => d.UpdatedAtUtc).ThenBy(d => d.Id),
            (DocumentSortField.UpdatedAt, true) => documents.OrderByDescending(d => d.UpdatedAtUtc).ThenBy(d => d.Id),
            (DocumentSortField.CreatedAt, false) => documents.OrderBy(d => d.CreatedAtUtc).ThenBy(d => d.Id),
            (DocumentSortField.CreatedAt, true) => documents.OrderByDescending(d => d.CreatedAtUtc).ThenBy(d => d.Id),
            (DocumentSortField.Status, false) => documents.OrderBy(d => d.Status).ThenBy(d => d.NormalizedDocumentNumber),
            (DocumentSortField.Status, true) => documents.OrderByDescending(d => d.Status).ThenBy(d => d.NormalizedDocumentNumber),
            (_, true) => documents.OrderByDescending(d => d.NormalizedDocumentNumber).ThenBy(d => d.Id),
            _ => documents.OrderBy(d => d.NormalizedDocumentNumber).ThenBy(d => d.Id),
        };

        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var total = await documents.CountAsync(cancellationToken);
        var rows = await documents
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DocumentRow(d, d.Versions.Count, d.Versions.FirstOrDefault(v => v.Id == d.CurrentVersionId)))
            .ToListAsync(cancellationToken);

        var permissions = ContractMapping.PermissionNames(projectAccess.Permissions);
        var items = await MapAsync(rows, projectAccess.Project.Code, permissions, cancellationToken);
        return new PagedResult<DocumentDto>(items, page, pageSize, total);
    }

    /// <summary>Недавно изменённые документы во всех доступных пользователю проектах организации.</summary>
    public async Task<IReadOnlyList<DocumentDto>> ListRecentAsync(Guid organizationId, int take, CancellationToken cancellationToken)
    {
        var organizationAccess = await access.RequireOrganizationAsync(organizationId, OrganizationPermission.View, cancellationToken);
        var userId = currentUser.RequireUserId();
        take = Math.Clamp(take, 1, MaxRecent);

        var memberships = await db.ProjectMemberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.Project!.OrganizationId == organizationId)
            .Select(m => new { m.ProjectId, m.Role })
            .ToListAsync(cancellationToken);
        var explicitRoles = memberships.ToDictionary(m => m.ProjectId, m => m.Role);

        IQueryable<Document> documents = db.Documents.AsNoTracking().Where(d => d.Project!.OrganizationId == organizationId);
        if (!organizationAccess.Permissions.Has(OrganizationPermission.ViewAllProjects))
        {
            var visibleProjects = explicitRoles.Keys.ToList();
            documents = documents.Where(d => visibleProjects.Contains(d.ProjectId));
        }

        var rows = await documents
            .OrderByDescending(d => d.UpdatedAtUtc)
            .Take(take)
            .Select(d => new DocumentRow(d, d.Versions.Count, d.Versions.FirstOrDefault(v => v.Id == d.CurrentVersionId)))
            .ToListAsync(cancellationToken);

        var projectIds = rows.Select(r => r.Document.ProjectId).Distinct().ToList();
        var projectCodes = await db.Projects.AsNoTracking()
            .Where(p => projectIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Code })
            .ToDictionaryAsync(p => p.Id, p => p.Code, cancellationToken);

        var result = new List<DocumentDto>(rows.Count);
        foreach (var group in rows.GroupBy(r => r.Document.ProjectId))
        {
            var role = AccessRules.EffectiveProjectRole(organizationAccess.Role, explicitRoles.GetValueOrDefault(group.Key)) ?? Dom.ProjectRole.Viewer;
            var permissions = ContractMapping.PermissionNames(AccessRules.PermissionsOf(role));
            result.AddRange(await MapAsync(group.ToList(), projectCodes.GetValueOrDefault(group.Key, string.Empty), permissions, cancellationToken));
        }

        return result.OrderByDescending(d => d.UpdatedAtUtc).ToList();
    }

    public async Task<DocumentDto> GetAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var (document, projectAccess) = await LoadAsync(documentId, ProjectPermission.View, track: false, cancellationToken);
        return await ToDtoAsync(document, projectAccess, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetDisciplinesAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await access.RequireProjectAsync(projectId, ProjectPermission.View, cancellationToken);
        return await db.Documents.AsNoTracking()
            .Where(d => d.ProjectId == projectId && d.Discipline != null)
            .Select(d => d.Discipline!)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentVersionDto>> GetVersionsAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var (document, _) = await LoadAsync(documentId, ProjectPermission.View, track: false, cancellationToken);
        var versions = await db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(cancellationToken);
        var names = await AuthorNamesAsync(versions, cancellationToken);
        return versions.Select(v => ToDto(v, document.CurrentVersionId, names)).ToList();
    }

    public async Task<DocumentVersionDto> GetVersionAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken)
    {
        var (document, _) = await LoadAsync(documentId, ProjectPermission.View, track: false, cancellationToken);
        var version = await db.DocumentVersions.AsNoTracking().FirstOrDefaultAsync(v => v.DocumentId == documentId && v.Id == versionId, cancellationToken)
            ?? throw new NotFoundException(VersionNotFound);
        var names = await AuthorNamesAsync([version], cancellationToken);
        return ToDto(version, document.CurrentVersionId, names);
    }

    // ---------- Изменение ----------

    /// <summary>Поиск документов проекта для сопоставления файлов: по точному номеру и/или имени файла текущей версии.</summary>
    public async Task<IReadOnlyList<DocumentDto>> LookupAsync(Guid projectId, string? documentNumber, string? fileName, CancellationToken cancellationToken)
    {
        var projectAccess = await access.RequireProjectAsync(projectId, ProjectPermission.View, cancellationToken);
        if (string.IsNullOrWhiteSpace(documentNumber) && string.IsNullOrWhiteSpace(fileName))
        {
            throw new ValidationException("documentNumber", "Укажите номер документа или имя файла.");
        }

        IQueryable<Document> documents = db.Documents.AsNoTracking().Where(d => d.ProjectId == projectId && d.Status != Dom.DocumentStatus.Archived);
        if (!string.IsNullOrWhiteSpace(documentNumber))
        {
            var normalized = Guard.NormalizeDocumentNumber(documentNumber);
            documents = documents.Where(d => d.NormalizedDocumentNumber == normalized);
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var name = fileName.Trim().ToLower();
            documents = documents.Where(d => d.Versions.Any(v => v.Id == d.CurrentVersionId && v.OriginalFileName.ToLower() == name));
        }

        var rows = await documents
            .OrderBy(d => d.NormalizedDocumentNumber)
            .Take(20)
            .Select(d => new DocumentRow(d, d.Versions.Count, d.Versions.FirstOrDefault(v => v.Id == d.CurrentVersionId)))
            .ToListAsync(cancellationToken);
        return await MapAsync(rows, projectAccess.Project.Code, ContractMapping.PermissionNames(projectAccess.Permissions), cancellationToken);
    }

    public async Task<DocumentDto> RegisterAsync(Guid projectId, RegisterDocumentRequest request, CancellationToken cancellationToken)
    {
        var projectAccess = await access.RequireProjectAsync(projectId, ProjectPermission.RegisterDocuments, cancellationToken);
        // Документ, зарегистрированный агентом, числится за пользователем, настроившим папку (у агента нет учётной записи).
        var userId = currentUser.IsAgent ? await AgentOwnerUserIdAsync(projectId, cancellationToken) : currentUser.RequireUserId();
        var document = Document.Create(projectId, request.DocumentNumber, request.Title, request.Description, request.Discipline, request.DocumentType, userId, clock.UtcNow);

        if (await db.Documents.AnyAsync(d => d.ProjectId == projectId && d.NormalizedDocumentNumber == document.NormalizedDocumentNumber, cancellationToken))
        {
            throw new ConflictException("Документ с таким номером уже зарегистрирован в проекте.");
        }

        db.Documents.Add(document);
        audit.Record(AuditActions.DocumentRegistered, AuditEntityTypes.Document, document.Id.ToString(), projectAccess.Project.OrganizationId, projectId,
            new { document.DocumentNumber, document.Title });
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(document, projectAccess, cancellationToken);
    }

    public async Task<DocumentDto> UpdateAsync(Guid documentId, UpdateDocumentRequest request, CancellationToken cancellationToken)
    {
        var (document, projectAccess) = await LoadAsync(documentId, ProjectPermission.RegisterDocuments, track: true, cancellationToken);
        if (request.RowVersion is < 0 or > uint.MaxValue)
        {
            throw new ValidationException("rowVersion", "Недопустимое значение версии записи.");
        }

        db.SetExpectedRowVersion(document, (uint)request.RowVersion);
        document.UpdateMetadata(request.Title, request.Description, request.Discipline, request.DocumentType, clock.UtcNow);
        audit.Record(AuditActions.DocumentUpdated, AuditEntityTypes.Document, document.Id.ToString(), projectAccess.Project.OrganizationId, document.ProjectId,
            new { document.Title, document.Discipline, document.DocumentType });
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(document, projectAccess, cancellationToken);
    }

    public async Task<DocumentDto> ArchiveAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var (document, projectAccess) = await LoadAsync(documentId, ProjectPermission.EditSettings, track: true, cancellationToken);
        document.Archive(clock.UtcNow);
        audit.Record(AuditActions.DocumentArchived, AuditEntityTypes.Document, document.Id.ToString(), projectAccess.Project.OrganizationId, document.ProjectId);
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(document, projectAccess, cancellationToken);
    }

    public async Task<DocumentDto> RestoreAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var (document, projectAccess) = await LoadAsync(documentId, ProjectPermission.EditSettings, track: true, cancellationToken);
        document.Restore(clock.UtcNow);
        audit.Record(AuditActions.DocumentRestored, AuditEntityTypes.Document, document.Id.ToString(), projectAccess.Project.OrganizationId, document.ProjectId);
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(document, projectAccess, cancellationToken);
    }

    /// <summary>
    /// Загрузка новой версии: файл сохраняется в хранилище, затем в транзакции под блокировкой строки документа
    /// выдаётся следующий номер версии и регистрируется запись. Повтор с тем же ключом идемпотентности возвращает существующую версию.
    /// </summary>
    public async Task<UploadVersionResult> UploadVersionAsync(Guid documentId, UploadVersionCommand command, CancellationToken cancellationToken)
    {
        var idempotencyKey = Guard.Required(command.IdempotencyKey, "idempotencyKey", "Ключ идемпотентности", DocumentVersion.IdempotencyKeyMaxLength);
        var fileName = Guard.FileName(command.FileName);
        var expectedHash = string.IsNullOrWhiteSpace(command.ExpectedSha256Hash) ? null : Guard.Sha256(command.ExpectedSha256Hash);

        var (document, projectAccess) = await LoadAsync(documentId, ProjectPermission.UploadVersions, track: false, cancellationToken);
        Guid? userId = currentUser.IsAgent ? null : currentUser.RequireUserId();
        var agentId = currentUser.IsAgent ? currentUser.RequireAgentId() : (Guid?)null;

        var existing = await FindByIdempotencyKeyAsync(documentId, idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return new UploadVersionResult(await ToVersionDtoAsync(existing, document.CurrentVersionId, cancellationToken), Created: false);
        }

        if (!Domain.Workflow.DocumentWorkflow.AllowsNewVersion(document.Status))
        {
            throw new DomainRuleException("Документ находится в архиве: загрузка новых версий невозможна. Сначала восстановите документ.");
        }

        var now = clock.UtcNow;
        var versionId = Guid.CreateVersion7();
        var storageKey = StorageKeys.Create(documentId, versionId, fileName, now);
        var stored = await storage.SaveAsync(storageKey, command.Content, cancellationToken);

        if (expectedHash is not null && !string.Equals(expectedHash, stored.Sha256Hash, StringComparison.Ordinal))
        {
            await DeleteQuietlyAsync(storageKey);
            throw new ValidationException("sha256Hash", "Контрольная сумма загруженного файла не совпадает с заявленной. Файл отклонён, повторите загрузку.");
        }

        try
        {
            await using var transaction = await db.BeginTransactionAsync(cancellationToken);
            await db.LockDocumentAsync(documentId, cancellationToken);

            var duplicate = await FindByIdempotencyKeyAsync(documentId, idempotencyKey, cancellationToken);
            if (duplicate is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                await DeleteQuietlyAsync(storageKey);
                return new UploadVersionResult(await ToVersionDtoAsync(duplicate, document.CurrentVersionId, cancellationToken), Created: false);
            }

            var tracked = await db.Documents.FirstAsync(d => d.Id == documentId, cancellationToken);
            var previousCurrent = tracked.CurrentVersionId is Guid previousId
                ? await db.DocumentVersions.FirstOrDefaultAsync(v => v.Id == previousId, cancellationToken)
                : null;
            var nextNumber = (await db.DocumentVersions.Where(v => v.DocumentId == documentId).MaxAsync(v => (int?)v.VersionNumber, cancellationToken) ?? 0) + 1;
            var version = tracked.RegisterVersion(versionId, nextNumber, fileName, storageKey, command.ContentType, stored.Size, stored.Sha256Hash, userId, agentId, command.Comment, idempotencyKey, now);
            // Ключ версии задаётся доменом: без явного Add EF счёл бы сущность существующей (Modified) и выполнил UPDATE вместо INSERT.
            db.DocumentVersions.Add(version);

            // Согласование не наследуется: непроверенная прежняя версия вытесняется, открытые задачи по документу отменяются.
            previousCurrent?.Supersede();
            var openTasks = await db.WorkflowTasks.Where(t => t.DocumentId == documentId && t.Status == Dom.WorkflowTaskStatus.Open).ToListAsync(cancellationToken);
            foreach (var task in openTasks)
            {
                task.Cancel(userId, now, $"Загружена новая версия № {version.VersionNumber}");
            }

            audit.Record(AuditActions.DocumentVersionUploaded, AuditEntityTypes.DocumentVersion, version.Id.ToString(), projectAccess.Project.OrganizationId, document.ProjectId,
                new { documentId, version.VersionNumber, version.OriginalFileName, version.FileSize, version.Sha256Hash });
            // Сначала сохраняется строка версии, затем документ получает ссылку на неё (взаимные внешние ключи таблиц).
            await db.SaveChangesAsync(cancellationToken);
            tracked.MakeCurrent(version);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation("Загружена версия {Version} документа {DocumentId} ({Size} байт)", version.VersionNumber, documentId, version.FileSize);
            return new UploadVersionResult(await ToVersionDtoAsync(version, version.Id, cancellationToken), Created: true);
        }
        catch
        {
            await DeleteQuietlyAsync(storageKey);
            throw;
        }
    }

    public async Task<VersionContent> OpenVersionContentAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken)
    {
        var (document, projectAccess) = await LoadAsync(documentId, ProjectPermission.View, track: false, cancellationToken);
        var version = await db.DocumentVersions.AsNoTracking().FirstOrDefaultAsync(v => v.DocumentId == documentId && v.Id == versionId, cancellationToken)
            ?? throw new NotFoundException(VersionNotFound);

        var content = await storage.OpenReadAsync(version.StorageKey, cancellationToken);
        audit.Record(AuditActions.DocumentVersionDownloaded, AuditEntityTypes.DocumentVersion, version.Id.ToString(), projectAccess.Project.OrganizationId, document.ProjectId,
            new { documentId, version.VersionNumber });
        await db.SaveChangesAsync(cancellationToken);

        var names = await AuthorNamesAsync([version], cancellationToken);
        return new VersionContent(ToDto(version, document.CurrentVersionId, names), content);
    }

    // ---------- Внутренние помощники ----------

    private sealed record DocumentRow(Document Document, int VersionCount, DocumentVersion? CurrentVersion);

    private async Task<Guid> AgentOwnerUserIdAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var agentId = currentUser.RequireAgentId();
        var owner = await db.WatchedFolders.AsNoTracking()
            .Where(f => f.AgentInstallationId == agentId && f.ProjectId == projectId && f.IsEnabled && f.AutoRegisterUnknown)
            .OrderBy(f => f.CreatedAtUtc)
            .Select(f => (Guid?)f.CreatedByUserId)
            .FirstOrDefaultAsync(cancellationToken);
        return owner ?? throw new ForbiddenException(AccessService.AgentForbidden);
    }

    private async Task<(Document Document, ProjectAccess Access)> LoadAsync(Guid documentId, ProjectPermission permission, bool track, CancellationToken cancellationToken)
    {
        IQueryable<Document> query = track ? db.Documents : db.Documents.AsNoTracking();
        var document = await query.FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)
            ?? throw new NotFoundException(DocumentNotFound);
        var projectAccess = await access.RequireProjectAsync(document.ProjectId, permission, cancellationToken);
        return (document, projectAccess);
    }

    private Task<DocumentVersion?> FindByIdempotencyKeyAsync(Guid documentId, string idempotencyKey, CancellationToken cancellationToken) =>
        db.DocumentVersions.AsNoTracking().FirstOrDefaultAsync(v => v.DocumentId == documentId && v.IdempotencyKey == idempotencyKey, cancellationToken);

    private async Task DeleteQuietlyAsync(string storageKey)
    {
        try
        {
            await storage.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось удалить незавершённый объект хранилища {StorageKey}", storageKey);
        }
    }

    private async Task<DocumentDto> ToDtoAsync(Document document, ProjectAccess projectAccess, CancellationToken cancellationToken)
    {
        var versionCount = await db.DocumentVersions.CountAsync(v => v.DocumentId == document.Id, cancellationToken);
        var current = document.CurrentVersionId is null
            ? null
            : await db.DocumentVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == document.CurrentVersionId, cancellationToken);
        var rows = new List<DocumentRow> { new(document, versionCount, current) };
        var items = await MapAsync(rows, projectAccess.Project.Code, ContractMapping.PermissionNames(projectAccess.Permissions), cancellationToken);
        return items[0];
    }

    private async Task<DocumentVersionDto> ToVersionDtoAsync(DocumentVersion version, Guid? currentVersionId, CancellationToken cancellationToken) =>
        ToDto(version, currentVersionId, await AuthorNamesAsync([version], cancellationToken));

    /// <summary>Имена авторов версий: пользователи и агенты (с пометкой «Агент»).</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> AuthorNamesAsync(IEnumerable<DocumentVersion> versions, CancellationToken cancellationToken)
    {
        var list = versions.ToList();
        var names = new Dictionary<Guid, string>(await UserNamesAsync(list.Select(v => v.CreatedByUserId), cancellationToken));
        var agentIds = list.Where(v => v.CreatedByAgentId.HasValue).Select(v => v.CreatedByAgentId!.Value).Distinct().ToList();
        if (agentIds.Count > 0)
        {
            var agents = await db.AgentInstallations.AsNoTracking()
                .Where(a => agentIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Name })
                .ToListAsync(cancellationToken);
            foreach (var agent in agents)
            {
                names[agent.Id] = "Агент: " + agent.Name;
            }
        }

        return names;
    }

    private async Task<List<DocumentDto>> MapAsync(IReadOnlyList<DocumentRow> rows, string projectCode, IReadOnlyList<string> permissions, CancellationToken cancellationToken)
    {
        var userIds = rows.Select(r => (Guid?)r.Document.CreatedByUserId).Concat(rows.Select(r => r.CurrentVersion?.CreatedByUserId));
        var names = new Dictionary<Guid, string>(await UserNamesAsync(userIds, cancellationToken));
        foreach (var pair in await AuthorNamesAsync(rows.Where(r => r.CurrentVersion is not null).Select(r => r.CurrentVersion!), cancellationToken))
        {
            names[pair.Key] = pair.Value;
        }
        return rows.Select(r => new DocumentDto(
            r.Document.Id,
            r.Document.ProjectId,
            projectCode,
            r.Document.DocumentNumber,
            r.Document.Title,
            r.Document.Description,
            r.Document.Discipline,
            r.Document.DocumentType,
            ToApi(r.Document.Status),
            r.CurrentVersion is null ? null : ToDto(r.CurrentVersion, r.Document.CurrentVersionId, names),
            r.VersionCount,
            r.Document.CreatedAtUtc,
            names.GetValueOrDefault(r.Document.CreatedByUserId, string.Empty),
            r.Document.UpdatedAtUtc,
            r.Document.RowVersion,
            permissions)).ToList();
    }

    private static DocumentVersionDto ToDto(DocumentVersion version, Guid? currentVersionId, IReadOnlyDictionary<Guid, string> names) => new(
        version.Id,
        version.DocumentId,
        version.VersionNumber,
        version.OriginalFileName,
        version.ContentType,
        version.FileSize,
        version.Sha256Hash,
        (VersionStatus)(int)version.Status,
        version.Comment,
        version.CreatedAtUtc,
        version.CreatedByUserId,
        version.CreatedByUserId is Guid author
            ? names.GetValueOrDefault(author, string.Empty)
            : version.CreatedByAgentId is Guid agent ? names.GetValueOrDefault(agent, "Агент") : "Агент",
        version.Id == currentVersionId);

    private async Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid?> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
    }

    private static DocumentStatus ToApi(Dom.DocumentStatus status) => (DocumentStatus)(int)status;

    private static Dom.DocumentStatus ToDomain(DocumentStatus status)
    {
        var value = (Dom.DocumentStatus)(int)status;
        return Enum.IsDefined(value) ? value : throw new ValidationException("status", "Недопустимый статус документа.");
    }
}
