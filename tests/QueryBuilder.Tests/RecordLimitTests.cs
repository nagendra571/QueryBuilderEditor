using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataSources;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Editor;

namespace QueryBuilder.Tests;

public class RecordLimitTests
{
    [Theory]
    [InlineData(500, 2000, 500)]
    [InlineData(null, 2000, 2000)]
    [InlineData(500, null, 500)]
    [InlineData(null, null, null)]
    public void Data_source_limit_wins_over_host_default(int? dataSource, int? hostDefault, int? expected) =>
        Assert.Equal(expected, RecordLimits.Effective(dataSource, new RecordLimitSettings(hostDefault)));

    [Theory]
    [InlineData(5000, 1000, 5000)]     // limit set: the browser's requested rows are ignored
    [InlineData(5, 1000, 5)]
    [InlineData(null, 1000, 1000)]     // no limit: legacy behavior, honor the request...
    [InlineData(null, 50_000, 1000)]   // ...within the legacy 10,000 ceiling
    [InlineData(null, null, 1000)]
    public void Grid_rows(int? limit, int? requested, int expected) =>
        Assert.Equal(expected, RecordLimits.GridRows(limit, requested));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_001)]
    public void Invalid_host_default_throws_at_startup(int value) =>
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddQueryBuilderEditor(o =>
        {
            o.ConnectionString = "Server=(localdb)\\none;Database=none";
            o.DefaultMaxRecords = value;
        }));

    [Fact]
    public void Valid_host_default_is_accepted() =>
        new ServiceCollection().AddQueryBuilderEditor(o =>
        {
            o.ConnectionString = "Server=(localdb)\\none;Database=none";
            o.DefaultMaxRecords = 100_000;
        });

    [Fact]
    public async Task Run_uses_the_limit_as_the_row_cap_and_reports_it()
    {
        var execution = new FakeExecution(truncated: true);
        var handler = new RunQueryCommandHandler(
            new FakeDataSources(maxRecords: 5), null!, new NoopCatalog(), new FakeBuilders(), execution,
            new NoScopeGuard(), new UnrestrictedScope(), new RecordLimitSettings(null), new NoopAudit());

        var result = await handler.Handle(new RunQueryCommand(TestData.DataSourceId, TestData.Definition(), [], 1000, null), default);

        Assert.Equal(5, execution.RequestedMaxRows);
        Assert.Equal(5, result.RowLimit);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task Export_over_the_limit_is_refused_with_a_narrow_it_down_message()
    {
        var handler = ExportHandler(new FakeDataSources(maxRecords: 5), new FakeExecution(truncated: true), hostDefault: null);

        var ex = await Assert.ThrowsAsync<RecordLimitExceededException>(() => handler.Handle(ExportCommand(), default));
        Assert.Contains("more than 5 records", ex.Message);
        Assert.Contains("filters", ex.Message);
    }

    [Fact]
    public async Task Export_within_the_limit_succeeds()
    {
        var execution = new FakeExecution(truncated: false);
        var handler = ExportHandler(new FakeDataSources(maxRecords: null), execution, hostDefault: 2000);

        await handler.Handle(ExportCommand(), default);

        Assert.Equal(2000, execution.RequestedMaxRows);
    }

    [Fact]
    public async Task Export_without_any_limit_keeps_the_legacy_silent_cap()
    {
        var execution = new FakeExecution(truncated: true);
        var handler = ExportHandler(new FakeDataSources(maxRecords: null), execution, hostDefault: null);

        await handler.Handle(ExportCommand(), default);

        Assert.Equal(RecordLimits.LegacyExportRows, execution.RequestedMaxRows);
    }

    private static ExportQueryCommand ExportCommand() =>
        new(TestData.DataSourceId, TestData.Definition(), [], ExportFormat.Csv, "t", null);

    private static ExportQueryCommandHandler ExportHandler(FakeDataSources dataSources, FakeExecution execution, int? hostDefault) =>
        new(dataSources, null!, new NoopCatalog(), new FakeBuilders(), execution, new NoopExport(),
            new NoScopeGuard(), new UnrestrictedScope(), new RecordLimitSettings(hostDefault), new NoopAudit());

    private sealed class FakeDataSources(int? maxRecords) : IDataSourceRepository
    {
        public Task<DataSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<DataSource?>(new DataSource { Id = id, Name = "Test", MaxRecords = maxRecords });
        public Task<List<DataSource>> GetActiveAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<List<DataSource>> GetAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Update(DataSource dataSource) => throw new NotSupportedException();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeExecution(bool truncated) : IQueryExecutionService
    {
        public int? RequestedMaxRows { get; private set; }

        public Task<QueryResultSet> ExecuteAsync(DataSource dataSource, GeneratedQuery generatedQuery,
            IReadOnlyDictionary<string, string?> runtimeParameterValues, int? maxRows, CancellationToken cancellationToken)
        {
            RequestedMaxRows = maxRows;
            return Task.FromResult(new QueryResultSet { Truncated = truncated });
        }
    }

    private sealed class FakeBuilders : IQuerySqlBuilderFactory
    {
        public IQuerySqlBuilder GetBuilder(DataSourceProvider provider) => new Infrastructure.Sql.SqlServerQuerySqlBuilder();
    }

    private sealed class NoopCatalog : IDataCatalogService
    {
        public Task<DataSourceCatalog> GetCatalogAsync(Guid dataSourceId, CancellationToken cancellationToken) => Task.FromResult(TestData.Catalog());
        public Task ValidateAsync(Guid dataSourceId, QueryDefinition definition, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<List<SchemaObjectMetadata>> GetRawObjectsAsync(Guid dataSourceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void InvalidateCatalogCache(Guid dataSourceId) { }
    }

    private sealed class NoScopeGuard : IDataScopeGuard
    {
        public Task<DataSourceCatalog> FilterCatalogAsync(DataSourceCatalog catalog, CancellationToken cancellationToken) => Task.FromResult(catalog);
        public Task<IReadOnlyList<ScopePredicate>> AuthorizeAsync(Guid dataSourceId, QueryDefinition definition, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScopePredicate>>([]);
    }

    private sealed class UnrestrictedScope : ICurrentDataScope
    {
        public bool IsEnabled => false;
        public IReadOnlyList<string> DeclaredKeys => [];
        public Task<ResolvedDataScope> GetAsync(CancellationToken cancellationToken) => Task.FromResult(ResolvedDataScope.Unrestricted);
    }

    private sealed class NoopExport : IExportService
    {
        public Task<byte[]> ExportAsync(QueryResultSet resultSet, ExportFormat format, string sheetOrFileName, CancellationToken cancellationToken) =>
            Task.FromResult(Array.Empty<byte>());
        public string GetContentType(ExportFormat format) => "text/csv";
        public string GetFileExtension(ExportFormat format) => ".csv";
    }

    private sealed class NoopAudit : IAuditLogger
    {
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
