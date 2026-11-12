using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Infrastructure.Persistence.Configurations;

public sealed class SeenUserConfiguration : IEntityTypeConfiguration<SeenUser>
{
    public void Configure(EntityTypeBuilder<SeenUser> builder)
    {
        builder.ToTable("SeenUsers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Email).HasMaxLength(320);
        builder.HasIndex(x => new { x.AppId, x.UserId }).IsUnique();
        builder.HasOne<App>().WithMany().HasForeignKey(x => x.AppId).OnDelete(DeleteBehavior.Restrict);
    }
}
