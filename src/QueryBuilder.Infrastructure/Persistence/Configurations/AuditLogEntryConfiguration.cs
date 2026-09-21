using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Infrastructure.Persistence.Configurations;

public sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("AuditLogEntries");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Actor).HasMaxLength(450).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityName).HasMaxLength(200);
        builder.Property(x => x.Summary).HasMaxLength(500).IsRequired();
        builder.Property(x => x.DetailsJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.IpAddress).HasMaxLength(64);

        // Audit rows are immutable and never joined to SavedQuery — no FK, so history survives deletion.
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
        builder.HasIndex(x => x.Actor);
        builder.HasIndex(x => x.TimestampUtc);
    }
}
