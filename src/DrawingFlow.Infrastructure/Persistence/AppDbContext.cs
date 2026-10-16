using DrawingFlow.Application.Abstractions;
using DrawingFlow.Domain.Entities;
using DrawingFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace DrawingFlow.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMembership> ProjectMemberships => Set<ProjectMembership>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<AgentInstallation> AgentInstallations => Set<AgentInstallation>();
    public DbSet<WatchedFolder> WatchedFolders => Set<WatchedFolder>();
    public DbSet<SyncOperation> SyncOperations => Set<SyncOperation>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<WorkflowTask> WorkflowTasks => Set<WorkflowTask>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Database.BeginTransactionAsync(cancellationToken);

    public Task LockDocumentAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Documents\" WHERE \"Id\" = {documentId} FOR UPDATE", cancellationToken);

    public void SetExpectedRowVersion(Document document, uint rowVersion) =>
        Entry(document).Property(d => d.RowVersion).OriginalValue = rowVersion;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    /// <summary>
    /// Переводит ошибки СУБД в доменные исключения: нарушение уникальности и конкурентное изменение
    /// становятся <see cref="ConflictException"/> с понятным пользователю сообщением.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Данные были изменены другим пользователем. Обновите информацию и повторите попытку.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("Запись с такими данными уже существует.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            throw new ConflictException("Операция невозможна: на запись ссылаются другие данные.");
        }
    }
}
