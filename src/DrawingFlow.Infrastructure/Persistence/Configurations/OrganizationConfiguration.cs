using DrawingFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrawingFlow.Infrastructure.Persistence.Configurations;

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Code).HasMaxLength(50).IsRequired();
        builder.Property(o => o.NormalizedCode).HasMaxLength(50).IsRequired();
        builder.Property(o => o.Name).HasMaxLength(Organization.NameMaxLength).IsRequired();
        builder.HasIndex(o => o.NormalizedCode).IsUnique();
        builder.HasMany(o => o.Memberships).WithOne(m => m.Organization).HasForeignKey(m => m.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(o => o.Projects).WithOne(p => p.Organization).HasForeignKey(p => p.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OrganizationMembershipConfiguration : IEntityTypeConfiguration<OrganizationMembership>
{
    public void Configure(EntityTypeBuilder<OrganizationMembership> builder)
    {
        builder.ToTable("OrganizationMemberships");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(m => new { m.OrganizationId, m.UserId }).IsUnique();
        builder.HasIndex(m => m.UserId);
    }
}
