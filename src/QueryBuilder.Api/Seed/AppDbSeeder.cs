using Microsoft.EntityFrameworkCore;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Api.Seed;

public static class AppDbSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        if (await db.DataSources.AnyAsync(cancellationToken))
        {
            return;
        }

        db.DataSources.Add(new DataSource
        {
            Name = "Sales Sample",
            Description = "Demo sales schema (customers, orders, products) with two business-logic views.",
            Provider = DataSourceProvider.SqlServer,
            ConnectionStringName = "DemoSource",
            AllowedSchemas = ["sales"],
            ViewsOnly = true,
            IsActive = true,
            CreatedBy = "system-seed"
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
