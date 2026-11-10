using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Infrastructure.Persistence.Configurations;

public sealed class LaunchReplayConfiguration : IEntityTypeConfiguration<LaunchReplay>
{
    public void Configure(EntityTypeBuilder<LaunchReplay> builder)
    {
        builder.ToTable("LaunchReplays");
        // Per app, so one app's predictable ids can't make another app's launches look replayed.
        builder.HasKey(x => new { x.AppId, x.Jti });
        builder.Property(x => x.Jti).HasMaxLength(200);
        builder.HasIndex(x => x.ExpiresUtc);
    }
}
