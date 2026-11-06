namespace QueryBuilder.Tests.Postgres;

/// <summary>Runs only when QB_TEST_POSTGRES names a Postgres server whose login can create databases and roles.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "QB_TEST_POSTGRES";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable)))
        {
            Skip = $"Set {EnvironmentVariable} to a Postgres connection string to run this test.";
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresFactAttribute.EnvironmentVariable)))
        {
            Skip = $"Set {PostgresFactAttribute.EnvironmentVariable} to a Postgres connection string to run this test.";
        }
    }
}
