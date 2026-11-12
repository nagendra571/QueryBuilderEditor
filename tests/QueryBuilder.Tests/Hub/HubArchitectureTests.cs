using Xunit;

namespace QueryBuilder.Tests.Hub;

public class HubArchitectureTests
{
    // A hub-wide scope sees every app; each file here was reviewed for why it needs one. A new caller fails this test
    // until someone reviews it and adds it, and a file that stops using the factory must be removed so the list stays exact.
    private static readonly string[] Reviewed =
    [
        "src/QueryBuilder.Application/Abstractions/IHubWideScopeFactory.cs",
        "src/QueryBuilder.Application/DataSourceManagement/MoveDataSourceCommand.cs",
        "src/QueryBuilder.Application/Hub/Registry/GetHubAuditQuery.cs",
        "src/QueryBuilder.Application/Hub/Registry/RegistryCommands.cs",
        "src/QueryBuilder.Editor/Endpoints/HubDataSourceEndpoints.cs",
        "src/QueryBuilder.Editor/Endpoints/HubLaunchEndpoints.cs",
        "src/QueryBuilder.Editor/Endpoints/HubRegistryEndpoints.cs",
        "src/QueryBuilder.Editor/Endpoints/HubSuperAdminEndpoints.cs",
        "src/QueryBuilder.Editor/Hub/HubSessionCookie.cs",
        "src/QueryBuilder.Editor/Hub/SuperAdminCodeIssuer.cs",
        "src/QueryBuilder.Infrastructure/DependencyInjection.cs",
        "src/QueryBuilder.Infrastructure/Hub/LaunchTokenValidator.cs",
        "src/QueryBuilder.Infrastructure/Persistence/AppContextAccessor.cs",
        "src/QueryBuilder.Infrastructure/Persistence/HubWideScopeFactory.cs",
        "src/QueryBuilder.Infrastructure/Persistence/MetadataModeGuard.cs",
    ];

    [Fact]
    public void Only_reviewed_files_use_the_hub_wide_factory()
    {
        var root = FindRepoRoot();
        var src = Path.Combine(root, "src");
        var actual = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p is "obj" or "bin"))
            .Where(f => File.ReadAllText(f).Contains("IHubWideScopeFactory", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
            .ToHashSet(StringComparer.Ordinal);

        var extra = actual.Except(Reviewed).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var stale = Reviewed.Except(actual).OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.True(extra.Count == 0 && stale.Count == 0,
            "IHubWideScopeFactory callers changed. " +
            (extra.Count > 0 ? "New files (review them, then add to the list): " + string.Join(", ", extra) + ". " : "") +
            (stale.Count > 0 ? "Listed files that no longer reference it (remove them): " + string.Join(", ", stale) + "." : ""));
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (dir.GetFiles("*.sln*").Length > 0 || (Directory.Exists(Path.Combine(dir.FullName, "src")) && Directory.Exists(Path.Combine(dir.FullName, "tests"))))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }
}
