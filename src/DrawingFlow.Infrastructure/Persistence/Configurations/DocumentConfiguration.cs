using DrawingFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrawingFlow.Infrastructure.Persistence.Configurations;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("Documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.DocumentNumber).HasMaxLength(Document.NumberMaxLength).IsRequired();
        builder.Property(d => d.NormalizedDocumentNumber).HasMaxLength(Document.NumberMaxLength).IsRequired();
        builder.Property(d => d.Title).HasMaxLength(Document.TitleMaxLength).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(Document.DescriptionMaxLength);
        builder.Property(d => d.Discipline).HasMaxLength(Document.DisciplineMaxLength);
        builder.Property(d => d.DocumentType).HasMaxLength(Document.TypeMaxLength);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        // xmin PostgreSQL как токен оптимистичной блокировки.
        builder.Property(d => d.RowVersion).IsRowVersion();

        builder.HasIndex(d => new { d.ProjectId, d.NormalizedDocumentNumber }).IsUnique();
        builder.HasIndex(d => new { d.ProjectId, d.UpdatedAtUtc }).IsDescending(false, true);
        builder.HasIndex(d => new { d.ProjectId, d.Status });

        builder.HasOne(d => d.Project).WithMany().HasForeignKey(d => d.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(d => d.Versions).WithOne(v => v.Document).HasForeignKey(v => v.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DocumentVersion>().WithMany().HasForeignKey(d => d.CurrentVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.ToTable("DocumentVersions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.OriginalFileName).HasMaxLength(DocumentVersion.FileNameMaxLength).IsRequired();
        builder.Property(v => v.StorageKey).HasMaxLength(DocumentVersion.StorageKeyMaxLength).IsRequired();
        builder.Property(v => v.ContentType).HasMaxLength(DocumentVersion.ContentTypeMaxLength).IsRequired();
        builder.Property(v => v.Sha256Hash).HasMaxLength(DocumentVersion.Sha256Length).IsFixedLength().IsRequired();
        builder.Property(v => v.Comment).HasMaxLength(DocumentVersion.CommentMaxLength);
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(v => v.IdempotencyKey).HasMaxLength(DocumentVersion.IdempotencyKeyMaxLength).IsRequired();

        // Неизменяемость и идемпотентность обеспечиваются ограничениями СУБД, а не только кодом.
        builder.HasIndex(v => new { v.DocumentId, v.VersionNumber }).IsUnique();
        builder.HasIndex(v => new { v.DocumentId, v.IdempotencyKey }).IsUnique();
        builder.HasIndex(v => v.StorageKey).IsUnique();
        builder.HasIndex(v => v.Sha256Hash);
    }
}
