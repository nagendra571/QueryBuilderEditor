using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Infrastructure.Persistence.Configurations;

public sealed class QueryVisualizationConfiguration : IEntityTypeConfiguration<QueryVisualization>
{
    public void Configure(EntityTypeBuilder<QueryVisualization> builder)
    {
        builder.ToTable("QueryVisualizations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AppId).IsRequired();
        builder.HasOne<App>().WithMany().HasForeignKey(x => x.AppId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.OptionsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(x => x.UpdatedBy).HasMaxLength(450);

        builder.HasOne(x => x.SavedQuery)
            .WithMany()
            .HasForeignKey(x => x.SavedQueryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.SavedQueryId);
    }
}
