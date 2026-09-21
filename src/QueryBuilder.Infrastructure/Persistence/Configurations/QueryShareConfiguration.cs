using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Infrastructure.Persistence.Configurations;

public sealed class QueryShareConfiguration : IEntityTypeConfiguration<QueryShare>
{
    public void Configure(EntityTypeBuilder<QueryShare> builder)
    {
        builder.ToTable("QueryShares");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SharedWithUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.SharedWithUserEmail).HasMaxLength(256).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(x => x.UpdatedBy).HasMaxLength(450);

        builder.HasIndex(x => new { x.SavedQueryId, x.SharedWithUserId }).IsUnique();
    }
}
