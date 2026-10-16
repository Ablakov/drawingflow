using DrawingFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrawingFlow.Infrastructure.Persistence.Configurations;

public sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("Reviews");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Decision).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(r => r.Comment).HasMaxLength(Review.CommentMaxLength);
        builder.HasIndex(r => new { r.DocumentId, r.DecidedAtUtc }).IsDescending(false, true);
        builder.HasIndex(r => r.DocumentVersionId);
        builder.HasOne(r => r.Document).WithMany().HasForeignKey(r => r.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Version).WithMany().HasForeignKey(r => r.DocumentVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Reviewer).WithMany().HasForeignKey(r => r.ReviewerUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WorkflowTaskConfiguration : IEntityTypeConfiguration<WorkflowTask>
{
    public void Configure(EntityTypeBuilder<WorkflowTask> builder)
    {
        builder.ToTable("WorkflowTasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(t => t.Comment).HasMaxLength(WorkflowTask.CommentMaxLength);
        builder.HasIndex(t => new { t.AssigneeUserId, t.Status });
        builder.HasIndex(t => new { t.DocumentId, t.Status });
        builder.HasIndex(t => new { t.ProjectId, t.Status });
        builder.HasOne(t => t.Project).WithMany().HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.Document).WithMany().HasForeignKey(t => t.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.Version).WithMany().HasForeignKey(t => t.DocumentVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.Assignee).WithMany().HasForeignKey(t => t.AssigneeUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
