using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Commands.SavedQueries;
using QueryBuilder.Application.Dashboards;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Tests;

/// <summary>
/// Two queries with different audiences and a dashboard holding a chart from each plus a text tile:
/// <c>QuerySecret</c> is shared with nobody except the owner, <c>QueryOpen</c> with editor and viewer.
/// The dashboard itself is shared with editor, viewer and guest (guest has no query access at all).
/// </summary>
internal sealed class DashFixture
{
    public const string Owner = "owner";
    public const string EditorUser = "editor";
    public const string ViewerUser = "viewer";
    public const string Guest = "guest";
    public const string Stranger = "stranger";
    public const string SecretQueryName = "Salaries by department";
    public const string SecretChartName = "Payroll bars";

    public SavedQuery QuerySecret { get; }
    public SavedQuery QueryOpen { get; }
    public QueryVisualization VizSecret { get; }
    public QueryVisualization VizOpen { get; }
    public Dashboard Dashboard { get; }
    public DashboardTile TileSecret { get; }
    public DashboardTile TileOpen { get; }
    public DashboardTile TileText { get; }

    public FakeQueries Queries { get; }
    public VizFixture.FakeVisualizations Visualizations { get; } = new();
    public FakeDashboards Dashboards { get; }
    public VizFixture.FakeAudit Audit { get; } = new();
    public VizFixture.FakeUser User { get; } = new() { UserId = Owner };

    public DashFixture()
    {
        QuerySecret = NewQuery(SecretQueryName, Owner);
        QueryOpen = NewQuery("Open sales", Owner,
            new QueryShare { SharedWithUserId = EditorUser, AccessLevel = QueryAccessLevel.Editor },
            new QueryShare { SharedWithUserId = ViewerUser, AccessLevel = QueryAccessLevel.Viewer });
        Queries = new FakeQueries(QuerySecret, QueryOpen);

        VizSecret = AddViz(QuerySecret, SecretChartName);
        VizOpen = AddViz(QueryOpen, "Sales by region");

        Dashboard = new Dashboard
        {
            Name = "Ops overview",
            OwnerId = Owner,
            OwnerName = "Owner",
            Shares =
            [
                new DashboardShare { SharedWithUserId = EditorUser, AccessLevel = QueryAccessLevel.Editor },
                new DashboardShare { SharedWithUserId = ViewerUser, AccessLevel = QueryAccessLevel.Viewer },
                new DashboardShare { SharedWithUserId = Guest, AccessLevel = QueryAccessLevel.Viewer }
            ]
        };
        foreach (var s in Dashboard.Shares) s.DashboardId = Dashboard.Id;
        TileSecret = ChartTile(VizSecret, 0);
        TileOpen = ChartTile(VizOpen, 1);
        TileText = new DashboardTile { DashboardId = Dashboard.Id, Kind = DashboardTileKind.Text, Text = "Notes", Order = 2, X = 0, Y = 6, Width = 12, Height = 3 };
        Dashboard.Tiles.AddRange([TileSecret, TileOpen, TileText]);
        Dashboards = new FakeDashboards(this, Dashboard);
    }

    private static SavedQuery NewQuery(string name, string owner, params QueryShare[] shares) => new()
    {
        Name = name,
        OwnerId = owner,
        OwnerName = owner,
        DataSourceId = TestData.DataSourceId,
        Shares = [.. shares]
    };

    private QueryVisualization AddViz(SavedQuery query, string name)
    {
        var viz = new QueryVisualization { SavedQueryId = query.Id, SavedQuery = query, Name = name, Type = VisualizationType.Bar, OptionsJson = "{}" };
        Visualizations.Items.Add(viz);
        return viz;
    }

    public DashboardTile ChartTile(QueryVisualization viz, int order) => new()
    {
        DashboardId = Dashboard.Id,
        Kind = DashboardTileKind.Chart,
        VisualizationId = viz.Id,
        Visualization = viz,
        Order = order,
        X = order % 2 * 6,
        Y = order / 2 * 6,
        Width = 6,
        Height = 6
    };

    public static SaveDashboardTileRequest Req(DashboardTile t) =>
        new(t.Id, t.Kind, t.Order, t.X, t.Y, t.Width, t.Height, t.AutoHeight, t.Title, t.Text, t.VisualizationId);

    /// <summary>What the browser sends for an unchanged tile: a restricted tile has no visualization id to send.</summary>
    public static SaveDashboardTileRequest ReqAsClientSees(DashboardTile t, bool restricted) =>
        restricted ? new(t.Id, t.Kind, t.Order, t.X, t.Y, t.Width, t.Height, t.AutoHeight, null, null, null) : Req(t);

    public static SaveDashboardTileRequest NewText(string text, int order = 99) =>
        new(null, DashboardTileKind.Text, order, 0, 100, 6, 3, false, null, text, null);

    public static SaveDashboardTileRequest NewSection(string? title, string? description = null, int order = 99) =>
        new(null, DashboardTileKind.Section, order, 0, 100, 12, 1, false, title, description, null);

    public static SaveDashboardTileRequest NewChart(Guid vizId, int order = 99) =>
        new(null, DashboardTileKind.Chart, order, 6, 100, 6, 6, false, null, null, vizId);

    public SaveDashboardTilesCommandHandler SaveTilesHandler() =>
        new(Dashboards, Visualizations, Queries, User, Audit,
            new QueryBuilder.Application.Parameters.ParameterBinder(new NoOptions(), TimeProvider.System));

    private sealed class NoOptions : QueryBuilder.Application.Parameters.IParameterOptionsService
    {
        public Task<IReadOnlyList<QueryBuilder.Application.Parameters.ParameterOption>> GetOptionsAsync(QueryParameter parameter, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QueryBuilder.Application.Parameters.ParameterOption>>([]);
    }

    public ShareDashboardCommandHandler ShareHandler() =>
        new(Dashboards, Queries, new ShareQueryCommandHandler(Queries, User, Audit, new QueryBuilder.Application.Hub.ShareTargets(null!, null!)), User, Audit, new QueryBuilder.Application.Hub.ShareTargets(null!, null!));

    public sealed class FakeQueries(params SavedQuery[] items) : ISavedQueryRepository
    {
        public Task<SavedQuery?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(items.FirstOrDefault(q => q.Id == id));
        public Task<List<SavedQuery>> ListForUserAsync(QueryBuilder.Application.Common.AccessActor actor, CancellationToken cancellationToken) =>
            Task.FromResult(items.Where(q => q.OwnerId == actor.UserId || q.Shares.Any(s => s.SharedWithUserId == actor.UserId)).ToList());
        public Task AddAsync(SavedQuery q, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Update(SavedQuery q) => throw new NotSupportedException();
        public void Remove(SavedQuery q) => throw new NotSupportedException();
        public Task AddShareAsync(QueryShare share, CancellationToken cancellationToken)
        {
            items.First(q => q.Id == share.SavedQueryId).Shares.Add(share);
            return Task.CompletedTask;
        }
        public void RemoveShare(QueryShare share) => throw new NotSupportedException();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    public sealed class FakeDashboards(DashFixture fixture, params Dashboard[] items) : IDashboardRepository
    {
        public List<Dashboard> Items { get; } = [.. items];
        public int SaveCalls { get; private set; }

        public Task<Dashboard?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            var d = Items.FirstOrDefault(x => x.Id == id);
            // A real query would load these navigations; mimic that for tiles added in the same test.
            foreach (var t in d?.Tiles ?? [])
            {
                if (t.VisualizationId is { } vid && t.Visualization is null)
                {
                    t.Visualization = fixture.Visualizations.Items.FirstOrDefault(v => v.Id == vid);
                }
            }
            return Task.FromResult(d);
        }
        public Task<List<Dashboard>> ListForUserAsync(QueryBuilder.Application.Common.AccessActor actor, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Where(d => d.OwnerId == actor.UserId || d.Shares.Any(s => s.SharedWithUserId == actor.UserId)).ToList());
        public Task AddAsync(Dashboard dashboard, CancellationToken cancellationToken)
        {
            Items.Add(dashboard);
            return Task.CompletedTask;
        }
        public void Remove(Dashboard dashboard) => Items.Remove(dashboard);
        public Task AddTileAsync(DashboardTile tile, CancellationToken cancellationToken)
        {
            Items.First(d => d.Id == tile.DashboardId).Tiles.Add(tile);
            return Task.CompletedTask;
        }
        public void RemoveTile(DashboardTile tile) => Items.First(d => d.Id == tile.DashboardId).Tiles.Remove(tile);
        public Task AddShareAsync(DashboardShare share, CancellationToken cancellationToken)
        {
            Items.First(d => d.Id == share.DashboardId).Shares.Add(share);
            return Task.CompletedTask;
        }
        public void RemoveShare(DashboardShare share) => Items.First(d => d.Id == share.DashboardId).Shares.Remove(share);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCalls++;
            return Task.FromResult(0);
        }
    }
}
