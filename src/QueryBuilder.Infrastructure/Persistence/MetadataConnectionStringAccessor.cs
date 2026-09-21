namespace QueryBuilder.Infrastructure.Persistence;

/// <summary>
/// Holds the raw connection string QueryBuilder itself was registered with (the same value passed
/// to <c>AddInfrastructure</c>), so <see cref="ConnectionStringResolver"/> and the default data
/// source seeder can reuse it without Infrastructure depending on the Editor project's options type.
/// </summary>
public sealed class MetadataConnectionStringAccessor(string connectionString)
{
    public string ConnectionString { get; } = connectionString;
}
