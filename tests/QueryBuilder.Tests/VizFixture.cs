using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Infrastructure.Sql;

namespace QueryBuilder.Tests;

/// <summary>Hand-written fakes + a saved query with owner / Editor / Viewer shares, shared by the
/// visualization handler tests.</summary>
internal sealed class VizFixture
{
    public const string Owner = "owner";
    public const string EditorUser = "editor";
    public const string ViewerUser = "viewer";
    public const string Stranger = "stranger";

    public static QueryDefinition Definition() => new()
    {
        Source = new QuerySource { SchemaName = "sales", ObjectName = "vw_Orders", Alias = "t0" },
        Columns =
        [
            new QueryColumn { TableAlias = "t0", ColumnName = "RegionCode", Alias = "Region", DataType = ColumnDataType.Text },
            new QueryColumn { TableAlias = "t0", ColumnName = "Channel", DataType = ColumnDataType.Text },
            new QueryColumn { TableAlias = "t0", ColumnName = "Amount", Alias = "Total", Aggregate = AggregateFunction.Sum, DataType = ColumnDataType.Number },
            new QueryColumn { TableAlias = "t0", ColumnName = "OrderId", Alias = "Orders", Aggregate = AggregateFunction.Count, DataType = ColumnDataType.Number },
            new QueryColumn { TableAlias = "t0", ColumnName = "Amount", Alias = "Avg1", Aggregate = AggregateFunction.Avg, DataType = ColumnDataType.Number },
            new QueryColumn { TableAlias = "t0", ColumnName = "Amount", Alias = "Min1", Aggregate = AggregateFunction.Min, DataType = ColumnDataType.Number },
            new QueryColumn { TableAlias = "t0", ColumnName = "Amount", Alias = "Max1", Aggregate = AggregateFunction.Max, DataType = ColumnDataType.Number },
            new QueryColumn { TableAlias = "t0", ColumnName = "Note", Alias = "HiddenNote", DataType = ColumnDataType.Text, IsVisible = false }
        ]
    };

    public SavedQuery Query { get; }
    public FakeSavedQueries Queries { get; }
    public FakeVisualizations Visualizations { get; } = new();
    public FakeAudit Audit { get; } = new();
    public FakeUser User { get; } = new();
    public FakeCatalog Catalog { get; } = new();
    public FakeGuard Guard { get; } = new();
    public FakeBuilderFactory Builders { get; }
    public FakeExecutor Executor { get; } = new();
    public DataSource DataSource { get; } = new() { Name = "Sales" };
    public FakeDataSources DataSources { get; }
    public RecordLimitSettings Limits { get; set; } = new(null);

    public VizFixture()
    {
        DataSource.Id = TestData.DataSourceId;
        Query = new SavedQuery
        {
            Name = "Orders by region",
            DataSourceId = TestData.DataSourceId,
            DataSource = DataSource,
            OwnerId = Owner,
            OwnerName = "Owner",
            DefinitionJson = QueryDefinitionSerializer.Serialize(Definition()),
            Shares =
            [
                new QueryShare { SharedWithUserId = EditorUser, AccessLevel = QueryAccessLevel.Editor },
                new QueryShare { SharedWithUserId = ViewerUser, AccessLevel = QueryAccessLevel.Viewer }
            ]
        };
        Queries = new FakeSavedQueries(Query);
        DataSources = new FakeDataSources(DataSource);
        Builders = new FakeBuilderFactory(new SqlServerQuerySqlBuilder());
        User.UserId = Owner;
    }

    public QueryVisualization AddVisualization(VisualizationType type, VisualizationOptions options, Guid? queryId = null)
    {
        var viz = new QueryVisualization
        {
            SavedQueryId = queryId ?? Query.Id,
            Name = "Chart",
            Type = type,
            OptionsJson = VisualizationOptionsSerializer.Serialize(options)
        };
        Visualizations.Items.Add(viz);
        return viz;
    }

    public static VisualizationOptions BarOptions() => new() { X = "Region", Measures = ["Total"] };

    public static QueryResultSet Result(IEnumerable<Dictionary<string, object?>> rows, bool truncated = false, params (string Name, ColumnDataType Type)[] columns)
    {
        var list = rows.ToList();
        var cols = columns.Length > 0
            ? columns.Select(c => new ResultColumn { Name = c.Name, DataType = c.Type }).ToList()
            : list.FirstOrDefault()?.Keys.Select(k => new ResultColumn
            {
                Name = k,
                DataType = k is "Region" or "Channel" or "HiddenNote" ? ColumnDataType.Text : ColumnDataType.Number
            }).ToList() ?? [];
        return new QueryResultSet { Columns = cols, Rows = list, Truncated = truncated, ExecutionTimeMs = 3 };
    }

    public sealed class FakeUser : ICurrentUserService
    {
        public string UserId { get; set; } = string.Empty;
        public string DisplayName => UserId;
        public string? Email => null;
    }

    public sealed class FakeAudit : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = [];
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    public sealed class FakeSavedQueries(SavedQuery query) : ISavedQueryRepository
    {
        public Task<SavedQuery?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == query.Id ? query : null);
        public Task<List<SavedQuery>> GetForUserAsync(string userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(SavedQuery q, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Update(SavedQuery q) => throw new NotSupportedException();
        public void Remove(SavedQuery q) => throw new NotSupportedException();
        public Task AddShareAsync(QueryShare share, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void RemoveShare(QueryShare share) => throw new NotSupportedException();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    public sealed class FakeVisualizations : IVisualizationRepository
    {
        public List<QueryVisualization> Items { get; } = [];
        public int SaveCalls { get; private set; }

        public Task<List<QueryVisualization>> GetForQueryAsync(Guid savedQueryId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Where(v => v.SavedQueryId == savedQueryId).OrderBy(v => v.DisplayOrder).ToList());
        public Task<List<QueryVisualization>> GetForQueriesAsync(IReadOnlyCollection<Guid> savedQueryIds, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Where(v => savedQueryIds.Contains(v.SavedQueryId)).ToList());
        public Task<QueryVisualization?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(v => v.Id == id));
        public Task AddAsync(QueryVisualization visualization, CancellationToken cancellationToken)
        {
            Items.Add(visualization);
            return Task.CompletedTask;
        }
        public void Update(QueryVisualization visualization) { }
        public void Remove(QueryVisualization visualization) => Items.Remove(visualization);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCalls++;
            return Task.FromResult(0);
        }
    }

    public sealed class FakeDataSources(DataSource dataSource) : IDataSourceRepository
    {
        public Task<DataSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == dataSource.Id ? dataSource : null);
        public Task<List<DataSource>> GetActiveAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<List<DataSource>> GetAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(DataSource dataSource, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void UpdateOnly(DataSource dataSource, params string[] propertyNames) { }
        public void Update(DataSource ds) => throw new NotSupportedException();
        public Task<bool> TryReplaceEncryptedConnectionAsync(Guid id, string expected, string replacement, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    public sealed class FakeCatalog : IDataCatalogService
    {
        public int ValidateCalls { get; private set; }
        public Task<DataSourceCatalog> GetCatalogAsync(Guid dataSourceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ValidateAsync(Guid dataSourceId, QueryDefinition definition, CancellationToken cancellationToken)
        {
            ValidateCalls++;
            return Task.CompletedTask;
        }
        public Task<List<SchemaObjectMetadata>> GetRawObjectsAsync(Guid dataSourceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<List<AdminUnavailableObject>> GetUnavailableObjectsAsync(Guid dataSourceId, CancellationToken cancellationToken) => Task.FromResult(new List<AdminUnavailableObject>());
        public void InvalidateCatalogCache(Guid dataSourceId) { }
    }

    public sealed class FakeGuard : IDataScopeGuard
    {
        public IReadOnlyList<ScopePredicate> Predicates { get; set; } = [];
        public int AuthorizeCalls { get; private set; }
        public Task<DataSourceCatalog> FilterCatalogAsync(DataSourceCatalog catalog, CancellationToken cancellationToken) => Task.FromResult(catalog);
        public Task<IReadOnlyList<ScopePredicate>> AuthorizeAsync(Guid dataSourceId, QueryDefinition definition, CancellationToken cancellationToken)
        {
            AuthorizeCalls++;
            return Task.FromResult(Predicates);
        }
    }

    public sealed class FakeScope : ICurrentDataScope
    {
        public bool IsEnabled => true;
        public IReadOnlyList<string> DeclaredKeys => ["ProgramId"];
        public Task<ResolvedDataScope> GetAsync(CancellationToken cancellationToken) => Task.FromResult(ResolvedDataScope.Unrestricted);
    }

    /// <summary>Wraps the real SQL builder so tests can both inspect what <c>Build</c> received and read the SQL it produced.</summary>
    public sealed class FakeBuilderFactory(IQuerySqlBuilder inner) : IQuerySqlBuilderFactory, IQuerySqlBuilder
    {
        public IReadOnlyList<ScopePredicate>? ReceivedScope { get; private set; }
        public GeneratedQuery? Generated { get; private set; }
        public DataSourceProvider Provider => inner.Provider;
        public IQuerySqlBuilder GetBuilder(DataSourceProvider provider) => this;
        public GeneratedQuery Build(QueryDefinition definition, IReadOnlyList<ScopePredicate> scope)
        {
            ReceivedScope = scope;
            Generated = inner.Build(definition, scope);
            return Generated;
        }
    }

    public sealed class FakeExecutor : IQueryExecutionService
    {
        public QueryResultSet Result { get; set; } = new();
        public int? ReceivedMaxRows { get; private set; }
        public IReadOnlyDictionary<string, string?>? ReceivedParameters { get; private set; }
        public int Calls { get; private set; }

        public Task<QueryResultSet> ExecuteAsync(DataSource dataSource, GeneratedQuery generatedQuery,
            IReadOnlyDictionary<string, string?> runtimeParameterValues, int? maxRows, CancellationToken cancellationToken)
        {
            Calls++;
            ReceivedMaxRows = maxRows;
            ReceivedParameters = runtimeParameterValues;
            return Task.FromResult(Result);
        }
    }
}
