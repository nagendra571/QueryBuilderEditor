using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Infrastructure.Persistence.Configurations;

public sealed class DataProtectionKeyRecordConfiguration : IEntityTypeConfiguration<DataProtectionKeyRecord>
{
    public void Configure(EntityTypeBuilder<DataProtectionKeyRecord> builder)
    {
        builder.ToTable("DataProtectionKeys");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FriendlyName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Xml).IsRequired();
    }
}
