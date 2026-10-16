using DrawingFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrawingFlow.Infrastructure.Persistence.Configurations;

public sealed class AgentInstallationConfiguration : IEntityTypeConfiguration<AgentInstallation>
{
    public void Configure(EntityTypeBuilder<AgentInstallation> builder)
    {
        builder.ToTable("AgentInstallations");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).HasMaxLength(AgentInstallation.NameMaxLength).IsRequired();
        builder.Property(a => a.MachineName).HasMaxLength(AgentInstallation.MachineNameMaxLength);
        builder.Property(a => a.AgentVersion).HasMaxLength(AgentInstallation.VersionMaxLength);
        builder.Property(a => a.CredentialHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(a => a.CredentialPrefix).HasMaxLength(AgentInstallation.CredentialPrefixLength).IsRequired();
        builder.HasIndex(a => a.CredentialHash).IsUnique();
        builder.HasIndex(a => new { a.OrganizationId, a.Name });
        builder.HasOne(a => a.Organization).WithMany().HasForeignKey(a => a.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(a => a.Folders).WithOne(f => f.Agent).HasForeignKey(f => f.AgentInstallationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WatchedFolderConfiguration : IEntityTypeConfiguration<WatchedFolder>
{
    public void Configure(EntityTypeBuilder<WatchedFolder> builder)
    {
        builder.ToTable("WatchedFolders");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Path).HasMaxLength(WatchedFolder.PathMaxLength).IsRequired();
        builder.Property(f => f.NormalizedPath).HasMaxLength(WatchedFolder.PathMaxLength).IsRequired();
        builder.Property(f => f.IncludePatterns).HasMaxLength(WatchedFolder.PatternsMaxLength).IsRequired();
        builder.Property(f => f.ExcludePatterns).HasMaxLength(WatchedFolder.PatternsMaxLength);
        builder.Property(f => f.DocumentNumberPattern).HasMaxLength(WatchedFolder.NumberPatternMaxLength);
        builder.Property(f => f.MatchingRule).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(f => new { f.AgentInstallationId, f.NormalizedPath }).IsUnique();
        builder.HasIndex(f => f.ProjectId);
        builder.HasOne(f => f.Project).WithMany().HasForeignKey(f => f.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SyncOperationConfiguration : IEntityTypeConfiguration<SyncOperation>
{
    public void Configure(EntityTypeBuilder<SyncOperation> builder)
    {
        builder.ToTable("SyncOperations");
        builder.HasKey(o => o.Id);
        // Идентификатор задаёт агент (ключ идемпотентности), сервер его не генерирует.
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.FilePath).HasMaxLength(SyncOperation.PathMaxLength).IsRequired();
        builder.Property(o => o.FileName).HasMaxLength(DocumentVersion.FileNameMaxLength).IsRequired();
        builder.Property(o => o.Sha256Hash).HasMaxLength(64).IsFixedLength();
        builder.Property(o => o.LastError).HasMaxLength(SyncOperation.ErrorMaxLength);
        builder.Property(o => o.Candidates).HasColumnType("jsonb");
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(o => o.RequestedAction).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(o => new { o.OrganizationId, o.Status, o.UpdatedAtUtc }).IsDescending(false, false, true);
        builder.HasIndex(o => new { o.AgentInstallationId, o.UpdatedAtUtc }).IsDescending(false, true);
        builder.HasIndex(o => new { o.ProjectId, o.Status });
        builder.HasOne(o => o.Agent).WithMany().HasForeignKey(o => o.AgentInstallationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.WatchedFolder).WithMany().HasForeignKey(o => o.WatchedFolderId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(o => o.Project).WithMany().HasForeignKey(o => o.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}
