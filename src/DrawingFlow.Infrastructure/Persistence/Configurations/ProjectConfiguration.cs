using DrawingFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrawingFlow.Infrastructure.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Code).HasMaxLength(50).IsRequired();
        builder.Property(p => p.NormalizedCode).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(Project.NameMaxLength).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(Project.DescriptionMaxLength);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(p => new { p.OrganizationId, p.NormalizedCode }).IsUnique();
        builder.HasIndex(p => new { p.OrganizationId, p.Name });
        builder.HasMany(p => p.Memberships).WithOne(m => m.Project).HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ProjectMembershipConfiguration : IEntityTypeConfiguration<ProjectMembership>
{
    public void Configure(EntityTypeBuilder<ProjectMembership> builder)
    {
        builder.ToTable("ProjectMemberships");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(m => new { m.ProjectId, m.UserId }).IsUnique();
        builder.HasIndex(m => m.UserId);
        builder.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
