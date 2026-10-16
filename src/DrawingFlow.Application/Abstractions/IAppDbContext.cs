using DrawingFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DrawingFlow.Application.Abstractions;

/// <summary>Контекст данных приложения. Реализуется в Infrastructure (EF Core + PostgreSQL).</summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Organization> Organizations { get; }
    DbSet<OrganizationMembership> OrganizationMemberships { get; }
    DbSet<Project> Projects { get; }
    DbSet<ProjectMembership> ProjectMemberships { get; }
    DbSet<AuditEvent> AuditEvents { get; }
    DbSet<Document> Documents { get; }
    DbSet<DocumentVersion> DocumentVersions { get; }
    DbSet<AgentInstallation> AgentInstallations { get; }
    DbSet<WatchedFolder> WatchedFolders { get; }
    DbSet<SyncOperation> SyncOperations { get; }
    DbSet<Review> Reviews { get; }
    DbSet<WorkflowTask> WorkflowTasks { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Блокирует строку документа (SELECT … FOR UPDATE) до конца текущей транзакции: сериализует выдачу номеров версий.</summary>
    Task LockDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Задаёт ожидаемое значение токена конкурентности; расхождение при сохранении даёт конфликт.</summary>
    void SetExpectedRowVersion(Document document, uint rowVersion);
}
