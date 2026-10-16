using DrawingFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DrawingFlow.Infrastructure.Persistence.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityAlwaysColumn();
        builder.Property(e => e.ActorName).HasMaxLength(AuditEvent.ActorNameMaxLength).IsRequired();
        builder.Property(e => e.Action).HasMaxLength(AuditEvent.ActionMaxLength).IsRequired();
        builder.Property(e => e.EntityType).HasMaxLength(AuditEvent.EntityTypeMaxLength).IsRequired();
        builder.Property(e => e.EntityId).HasMaxLength(AuditEvent.EntityIdMaxLength);
        builder.Property(e => e.Details).HasColumnType("jsonb");
        builder.Property(e => e.ClientAddress).HasMaxLength(AuditEvent.ClientAddressMaxLength);
        builder.HasIndex(e => new { e.OrganizationId, e.OccurredAtUtc }).IsDescending(false, true);
        builder.HasIndex(e => new { e.ProjectId, e.OccurredAtUtc }).IsDescending(false, true);
        builder.HasIndex(e => e.ActorUserId);
    }
}
