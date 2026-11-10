using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Infrastructure.Persistence.Configurations;

public sealed class AppSigningKeyConfiguration : IEntityTypeConfiguration<AppSigningKey>
{
    public void Configure(EntityTypeBuilder<AppSigningKey> builder)
    {
        builder.ToTable("AppSigningKeys");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kid).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Algorithm).HasMaxLength(10).IsRequired();
        builder.Property(x => x.PublicKeyPem).HasMaxLength(4000).IsRequired();
        builder.HasIndex(x => new { x.AppId, x.Kid }).IsUnique();
        builder.HasOne<App>().WithMany().HasForeignKey(x => x.AppId).OnDelete(DeleteBehavior.Restrict);
    }
}
