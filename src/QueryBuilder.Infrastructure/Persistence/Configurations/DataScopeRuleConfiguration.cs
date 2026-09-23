using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Infrastructure.Persistence.Configurations;

public sealed class DataScopeRuleConfiguration : IEntityTypeConfiguration<DataScopeRule>
{
    public void Configure(EntityTypeBuilder<DataScopeRule> builder)
    {
        builder.ToTable("DataScopeRules");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ObjectName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ScopeKey).HasMaxLength(128);
        builder.Property(x => x.ColumnName).HasMaxLength(128);
        builder.Property(x => x.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(x => x.UpdatedBy).HasMaxLength(450);

        builder.HasOne<DataSource>()
            .WithMany()
            .HasForeignKey(x => x.DataSourceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.DataSourceId, x.ObjectName, x.ScopeKey }).IsUnique();
    }
}
