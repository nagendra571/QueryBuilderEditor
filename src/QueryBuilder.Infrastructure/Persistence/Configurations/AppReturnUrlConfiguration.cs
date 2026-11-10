using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Infrastructure.Persistence.Configurations;

public sealed class AppReturnUrlConfiguration : IEntityTypeConfiguration<AppReturnUrl>
{
    public void Configure(EntityTypeBuilder<AppReturnUrl> builder)
    {
        builder.ToTable("AppReturnUrls");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Url).HasMaxLength(2000).IsRequired();
        builder.HasIndex(x => new { x.AppId, x.Url }).IsUnique();
        builder.HasOne<App>().WithMany().HasForeignKey(x => x.AppId).OnDelete(DeleteBehavior.Restrict);
    }
}
