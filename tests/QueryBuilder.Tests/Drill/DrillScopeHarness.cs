using System.Globalization;
using System.Text.Json;
using CsvHelper;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Drill;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Editor;
using QueryBuilder.Infrastructure.Export;
using QueryBuilder.Infrastructure.Sql;

namespace QueryBuilder.Tests.Drill;

internal sealed record EnrolmentRow(
    int Id, int ProgramId, string? Status, string Site, decimal Amount, double Score, DateTime EnrolledOn, DateTimeOffset SeenAt, string Note);

/// <summary>60 enrolments over programs 10, 20 and 30, shared by the SQL Server and PostgreSQL zone tests so both prove the same
/// guarantee against the same expected answers. Fractions of a second and extreme floats are there on purpose: they are what a
/// value copied out of chart JSON has to survive.</summary>
internal static class EnrolmentSeed
{
    public static readonly string[] Sites = ["North", "South", "East", "West"];

    /// <param name="fractionTicks">100 ns ticks added to the date-times; PostgreSQL keeps microseconds, so it gets a multiple of 10.</param>
    public static List<EnrolmentRow> Rows(long fractionTicks)
    {
        var rows = new List<EnrolmentRow>();
        for (var id = 1; id <= 60; id++)
        {
            // Id 7 holds an empty string: it must stay apart from the NULL rows, as its own chart group.
            var status = id % 5 == 0 ? null : id == 7 ? "" : id % 2 == 0 ? "Active" : "Closed";
            var score = id % 10 == 0 ? 1.5E-05 : id % 9 == 0 ? 12345.678901234567 : ((id % 6) + 1) / 10.0;
            var enrolled = new DateTime(2024, 3, 1, 10, 30, 0, DateTimeKind.Unspecified).AddDays(id % 5)
                .AddTicks(id % 2 == 0 ? fractionTicks : id % 3 == 0 ? 5_000_000 : 0);
            var instant = new DateTimeOffset(2024, 3, 1, 5, 0, 0, TimeSpan.Zero).AddHours(id % 4).AddTicks(fractionTicks);
            var seen = instant.ToOffset(id % 2 == 0 ? TimeSpan.FromMinutes(330) : TimeSpan.FromHours(-8));
            rows.Add(new EnrolmentRow(
                id, 10 * ((id % 3) + 1), status, Sites[id % 4], ((id % 7) + 1) * 1.10m, score, enrolled, seen, $"note {id}"));
        }
        return rows;
    }
}

/// <summary>Who is asking: the programs their row-level scope allows, or null for an unrestricted user.</summary>
internal sealed record Persona(string Name, int[]? Programs)
{
    public static readonly Persona Unrestricted = new("Unrestricted", null);
    public static readonly Persona P10 = new("Program 10", [10]);
    public static readonly Persona P10And30 = new("Programs 10 and 30", [10, 30]);

    public bool Sees(EnrolmentRow row) => Programs is null || Programs.Contains(row.ProgramId);

    public IReadOnlyList<ScopePredicate> Predicates =>
        Programs is null ? [] : [new ScopePredicate("t0", "ProgramId", ColumnDataType.Number, Programs.Select(p => p.ToString(CultureInfo.InvariantCulture)).ToList())];
}

/// <summary>A click trail the matrix sends, and what it should leave of the data (before the persona's zone is applied).</summary>
internal sealed record StepCase(string Name, DrillStep[] Steps, Func<EnrolmentRow, bool> Matches)
{
    public static readonly StepCase[] All =
    [
        new("none", [], _ => true),
        new("Status=Active", [new DrillStep("Status", "Active")], r => r.Status == "Active"),
        new("Status=null", [new DrillStep("Status", null)], r => r.Status is null),
        new("tampered ProgramId=20", [new DrillStep("ProgramId", "20")], r => r.ProgramId == 20)
    ];
}

/// <summary>
/// The real handlers, SQL builder and execution service for one provider, over a throwaway database; only the persona's
/// scope predicates, the catalog and the repositories are stand-ins. Everything the browser could send goes through the same
/// commands the endpoints use.
/// </summary>
internal sealed class DrillHarness
{
    public const string Alias = "t0";

    public static readonly string[] DrillColumns = ["ProgramId", "Amount", "Score", "EnrolledOn", "SeenAt"];

    public DrillHarness(DataSourceProvider provider, string schema, string table, IQueryExecutionService executor, Persona persona)
    {
        Schema = schema;
        Table = table;
        Persona = persona;
        DataSource = new DataSource { Name = "Enrolments", Provider = provider };
        Catalog.Catalog = BuildCatalog(provider);

        IQuerySqlBuilder builder = provider == DataSourceProvider.PostgreSql ? new PostgresQuerySqlBuilder() : new SqlServerQuerySqlBuilder();
        Builders = new VizFixture.FakeBuilderFactory(builder);
        Guard.Predicates = persona.Predicates;
        User.UserId = VizFixture.Owner;

        Query = NewQuery("Enrolments by site", Aggregated());
        Queries = new VizFixture.FakeSavedQueries(Query);
        DataSources = new VizFixture.FakeDataSources(DataSource);
        Query.DataSourceId = DataSource.Id;
        Query.DataSource = DataSource;

        Plain = AddViz(Query, new VisualizationOptions { X = "Site", Measures = ["Enrolments"] });
        WithDrillColumns = AddViz(Query, new VisualizationOptions { X = "Site", Measures = ["Enrolments"], Drill = new DrillOptions { Columns = [.. DrillColumns] } });

        // A second chart on another saved query, to click on: a dashboard filter names a column of THIS chart, and may be
        // forged to name any column it lists as a drill column.
        SourceQuery = NewQuery("Statuses", Grouped("Status", ColumnDataType.Text));
        SourceQuery.DataSourceId = DataSource.Id;
        SourceViz = new QueryVisualization
        {
            SavedQueryId = SourceQuery.Id,
            SavedQuery = SourceQuery,
            Name = "Source chart",
            Type = VisualizationType.Bar,
            OptionsJson = VisualizationOptionsSerializer.Serialize(new VisualizationOptions
            {
                X = "Status", Measures = ["N"], Drill = new DrillOptions { Columns = ["ProgramId"] }
            })
        };

        Dashboard = new Dashboard { Name = "D", OwnerId = VizFixture.Owner, OwnerName = "Owner" };
        TargetTile = new DashboardTile { DashboardId = Dashboard.Id, Kind = DashboardTileKind.Chart, VisualizationId = Plain.Id, Visualization = Plain };
        SourceTile = new DashboardTile { DashboardId = Dashboard.Id, Kind = DashboardTileKind.Chart, VisualizationId = SourceViz.Id, Visualization = SourceViz };
        Dashboard.Tiles.AddRange([TargetTile, SourceTile]);
        Dashboards.Items.Add(Dashboard);

        Executor = executor;
    }

    public string Schema { get; }
    public string Table { get; }
    public Persona Persona { get; }
    public DataSource DataSource { get; }
    public VizFixture.FakeCatalog Catalog { get; } = new();
    public VizFixture.FakeGuard Guard { get; } = new();
    public VizFixture.FakeUser User { get; } = new();
    public VizFixture.FakeAudit Audit { get; } = new();
    public VizFixture.FakeVisualizations Visualizations { get; } = new();
    public VizFixture.FakeDashboardRepository Dashboards { get; } = new();
    public VizFixture.FakeBuilderFactory Builders { get; }
    public VizFixture.FakeSavedQueries Queries { get; }
    public VizFixture.FakeDataSources DataSources { get; }
    public IQueryExecutionService Executor { get; }
    public SavedQuery Query { get; }
    public SavedQuery SourceQuery { get; }
    public QueryVisualization Plain { get; }
    public QueryVisualization WithDrillColumns { get; }
    public QueryVisualization SourceViz { get; }
    public Dashboard Dashboard { get; }
    public DashboardTile TargetTile { get; }
    public DashboardTile SourceTile { get; }

    private DataSourceCatalog BuildCatalog(DataSourceProvider provider)
    {
        ColumnMetadata Col(string name, ColumnDataType type) => new() { Name = name, DataType = type };
        return new DataSourceCatalog
        {
            DataSourceId = DataSource.Id,
            Provider = provider,
            Schemas =
            [
                new SchemaMetadata
                {
                    Name = Schema,
                    Objects =
                    [
                        new SchemaObjectMetadata
                        {
                            SchemaName = Schema, Name = Table, Kind = SchemaObjectKind.Table,
                            Columns =
                            [
                                Col("Id", ColumnDataType.Number), Col("ProgramId", ColumnDataType.Number), Col("Status", ColumnDataType.Text),
                                Col("Site", ColumnDataType.Text), Col("Amount", ColumnDataType.Number), Col("Score", ColumnDataType.Number),
                                Col("EnrolledOn", ColumnDataType.DateTime), Col("SeenAt", ColumnDataType.DateTime), Col("Note", ColumnDataType.Text)
                            ]
                        }
                    ]
                }
            ]
        };
    }

    private SavedQuery NewQuery(string name, QueryDefinition definition) => new()
    {
        Name = name,
        DataSourceId = DataSource.Id,
        DataSource = DataSource,
        OwnerId = VizFixture.Owner,
        OwnerName = "Owner",
        DefinitionJson = QueryDefinitionSerializer.Serialize(definition)
    };

    private QueryVisualization AddViz(SavedQuery query, VisualizationOptions options)
    {
        var viz = new QueryVisualization
        {
            SavedQueryId = query.Id, Name = "Chart", Type = VisualizationType.Bar,
            OptionsJson = VisualizationOptionsSerializer.Serialize(options)
        };
        Visualizations.Items.Add(viz);
        return viz;
    }

    private QueryDefinition Source() => new()
    {
        Source = new QuerySource { SchemaName = Schema, ObjectName = Table, Alias = Alias, Kind = SchemaObjectKind.Table }
    };

    private QueryColumn PlainColumn(string name, ColumnDataType type) => new() { TableAlias = Alias, ColumnName = name, DataType = type };

    private QueryDefinition Aggregated()
    {
        var d = Source();
        d.Columns =
        [
            PlainColumn("Site", ColumnDataType.Text),
            PlainColumn("Status", ColumnDataType.Text),
            new QueryColumn { TableAlias = Alias, ColumnName = "Id", Alias = "Enrolments", Aggregate = AggregateFunction.Count, DataType = ColumnDataType.Number }
        ];
        return d;
    }

    /// <summary>A query that groups the table by one column and counts, so a chart row names a value and how many rows have it.</summary>
    public QueryDefinition Grouped(string column, ColumnDataType type)
    {
        var d = Source();
        d.Columns =
        [
            PlainColumn(column, type),
            new QueryColumn { TableAlias = Alias, ColumnName = "Id", Alias = "N", Aggregate = AggregateFunction.Count, DataType = ColumnDataType.Number }
        ];
        return d;
    }

    /// <summary>Adds a saved query and bar chart grouped by <paramref name="column"/>, returned as a standalone harness slice.</summary>
    public (SavedQuery Query, QueryVisualization Viz) AddGroupedChart(string column, ColumnDataType type)
    {
        var query = NewQuery($"By {column}", Grouped(column, type));
        var viz = AddViz(query, new VisualizationOptions { X = column, Measures = ["N"] });
        return (query, viz);
    }

    private VisualizationDataLoader Loader(SavedQuery? extraQuery = null)
    {
        var grant = new QueryBuilder.Application.Dashboards.DashboardGrant();
        var queries = extraQuery is null ? Queries : new VizFixture.FakeSavedQueries(extraQuery);
        return new VisualizationDataLoader(
            queries, Visualizations, DataSources, Catalog, Builders, Executor, Guard, new RecordLimitSettings(null),
            new FakeAppSettings(null), User,
            new QueryBuilder.Application.Parameters.ParameterBinder(null!, TimeProvider.System),
            grant, new QueryBuilder.Application.Dashboards.DashboardGrantResolver(Dashboards, grant));
    }

    private CrossFilterResolver Resolver() => new(Dashboards, DataSources, Catalog);

    public Task<DrillResultDto> DrillAsync(
        QueryVisualization viz, DrillAction action, DrillStep[] steps, string? breakdownColumn = null, SavedQuery? query = null) =>
        new DrillVisualizationCommandHandler(Loader(query), Catalog, Resolver(), new VizFixture.FakeScope(), Audit)
            .Handle(new DrillVisualizationCommand((query ?? Query).Id, viz.Id,
                new DrillRequest(action, steps.ToList(), breakdownColumn, null, null, null, null)), default);

    /// <summary>The one chart that drills into the table with ProgramId among its drill columns, downloaded as a CSV and parsed back.</summary>
    public async Task<(string[] Header, List<string[]> Rows)> ExportCsvAsync(QueryVisualization viz, DrillStep[] steps)
    {
        var exported = await new ExportDrillCommandHandler(
                Loader(), Catalog, Resolver(), new ExportService(), new VizFixture.FakeScope(), Audit)
            .Handle(new ExportDrillCommand(Query.Id, viz.Id,
                new DrillRequest(DrillAction.Rows, steps.ToList(), null, null, null, null, null), ExportFormat.Csv), default);

        using var reader = new CsvReader(new StringReader(System.Text.Encoding.UTF8.GetString(exported.Content)), CultureInfo.InvariantCulture);
        Assert.True(await reader.ReadAsync());
        reader.ReadHeader();
        var header = reader.HeaderRecord!;
        var rows = new List<string[]>();
        while (await reader.ReadAsync())
        {
            rows.Add(header.Select((_, i) => reader.GetField(i) ?? string.Empty).ToArray());
        }
        return (header, rows);
    }

    /// <summary>The target chart as the dashboard draws it, with the click-filters the browser would send for the source chart.</summary>
    public Task<VisualizationDataDto> RenderCrossFilteredAsync(params (string Column, string? Value)[] filters) =>
        new RenderVisualizationCommandHandler(Loader(), Resolver(), new VizFixture.FakeScope(), Audit)
            .Handle(new RenderVisualizationCommand(
                Query.Id, Plain.Id, [], Dashboard.Id, TargetTile.Id,
                filters.Select(f => new CrossFilterDto(SourceTile.Id, f.Column, f.Value)).ToList()), default);

    public Task<VisualizationDataDto> RenderAsync(SavedQuery query, QueryVisualization viz) =>
        new RenderVisualizationCommandHandler(Loader(query), Resolver(), new VizFixture.FakeScope(), Audit)
            .Handle(new RenderVisualizationCommand(query.Id, viz.Id, []), default);

    /// <summary>The value a browser would copy from a chart row: the row goes out as the endpoint's JSON and the cell comes back
    /// as the text of its JSON value, so what is tested is the wire format and not the CLR object behind it.</summary>
    public static string? ValueFromChartJson(Dictionary<string, object?> row, string column)
    {
        var json = JsonSerializer.Serialize(new { rows = new[] { row } }, QueryBuilderJson.Options);
        using var doc = JsonDocument.Parse(json);
        var cell = doc.RootElement.GetProperty("rows")[0].GetProperty(column);
        return cell.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => cell.GetString(),
            _ => cell.GetRawText()
        };
    }
}
