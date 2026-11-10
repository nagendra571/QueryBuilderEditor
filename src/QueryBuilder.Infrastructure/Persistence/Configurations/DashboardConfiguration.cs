using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Infrastructure.Persistence.Configurations;

public sealed class DashboardConfiguration : IEntityTypeConfiguration<Dashboard>
{
    public void Configure(EntityTypeBuilder<Dashboard> builder)
    {
        builder.ToTable("Dashboards");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AppId).IsRequired();
        builder.HasOne<App>().WithMany().HasForeignKey(x => x.AppId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.AppId, x.OwnerId });

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.OwnerId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.OwnerName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(x => x.UpdatedBy).HasMaxLength(450);
        builder.Property(x => x.Version).IsConcurrencyToken();
        // Rows that predate the grid get 1 from the column default, so they are converted on read.
        builder.Property(x => x.GridVersion).HasDefaultValue(1);
        builder.Property(x => x.ParametersJson);

        builder.HasMany(x => x.Tiles).WithOne(t => t.Dashboard).HasForeignKey(t => t.DashboardId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Shares).WithOne(s => s.Dashboard).HasForeignKey(s => s.DashboardId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.OwnerId);
    }
}

public sealed class DashboardTileConfiguration : IEntityTypeConfiguration<DashboardTile>
{
    public void Configure(EntityTypeBuilder<DashboardTile> builder)
    {
        builder.ToTable("DashboardTiles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AppId).IsRequired();
        builder.HasOne<App>().WithMany().HasForeignKey(x => x.AppId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(x => x.X).HasDefaultValue(0);
        builder.Property(x => x.Y).HasDefaultValue(0);
        builder.Property(x => x.AutoHeight).HasDefaultValue(false);
        builder.Property(x => x.ParameterMappingsJson);
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.Text).HasMaxLength(2000);
        builder.Property(x => x.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(x => x.UpdatedBy).HasMaxLength(450);

        // Deleting a chart (or its query) empties the tile instead of removing it, so the layout survives.
        builder.HasOne(x => x.Visualization)
            .WithMany()
            .HasForeignKey(x => x.VisualizationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.DashboardId);
    }
}

public sealed class DashboardShareConfiguration : IEntityTypeConfiguration<DashboardShare>
{
    public void Configure(EntityTypeBuilder<DashboardShare> builder)
    {
        builder.ToTable("DashboardShares");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AppId).IsRequired();
        builder.HasOne<App>().WithMany().HasForeignKey(x => x.AppId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.SharedWithUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.SharedWithUserEmail).HasMaxLength(256).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(x => x.UpdatedBy).HasMaxLength(450);

        builder.HasIndex(x => new { x.DashboardId, x.SharedWithUserId }).IsUnique();
    }
}
