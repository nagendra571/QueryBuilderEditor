using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Infrastructure.Persistence.Configurations;

public sealed class LaunchReplayConfiguration : IEntityTypeConfiguration<LaunchReplay>
{
    public void Configure(EntityTypeBuilder<LaunchReplay> builder)
    {
        builder.ToTable("LaunchReplays");
        builder.HasKey(x => x.Jti);
        builder.Property(x => x.Jti).HasMaxLength(200);
        builder.HasIndex(x => x.ExpiresUtc);
    }
}
