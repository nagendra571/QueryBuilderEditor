using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Infrastructure.Persistence.Configurations;

public sealed class MetadataSettingConfiguration : IEntityTypeConfiguration<MetadataSetting>
{
    public void Configure(EntityTypeBuilder<MetadataSetting> builder)
    {
        builder.ToTable("MetadataSettings");
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasMaxLength(64);
        builder.Property(x => x.Value).HasMaxLength(400).IsRequired();
    }
}
