using Microsoft.EntityFrameworkCore;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Api.Seed;

public static class AppDbSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        if (await db.DataSources.AnyAsync(x => x.ConnectionStringName == "DemoSource", cancellationToken))
        {
            return;
        }

        // QueryBuilder's own fallback seeder (DefaultDataSourceSeeder, runs first — see Program.cs)
        // may already have registered a "Default" data source pointed at this app's own metadata DB.
        // This sample app always has something more interesting to show, so replace it.
        var autoDefaults = await db.DataSources
            .Where(x => x.ConnectionStringName == DataSource.DefaultConnectionStringSentinel)
            .ToListAsync(cancellationToken);
        db.DataSources.RemoveRange(autoDefaults);

        db.DataSources.Add(new DataSource
        {
            Name = "Sales Sample",
            Description = "Demo sales schema (customers, orders, products) with two business-logic views.",
            Provider = DataSourceProvider.SqlServer,
            ConnectionStringName = "DemoSource",
            AllowedSchemas = ["sales"],
            CatalogScope = CatalogScope.Views,
            IsActive = true,
            CreatedBy = "system-seed"
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
