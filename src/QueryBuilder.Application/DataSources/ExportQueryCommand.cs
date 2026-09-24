using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.DataSources;

public sealed record ExportQueryCommand(
    Guid DataSourceId,
    QueryDefinition Definition,
    Dictionary<string, string?> ParameterValues,
    ExportFormat Format,
    string FileName,
    Guid? SavedQueryId) : IRequest<ExportResult>;

public sealed record ExportResult(byte[] Content, string ContentType, string FileName);

public sealed class ExportQueryCommandHandler(
    IDataSourceRepository dataSourceRepository,
    ISavedQueryRepository savedQueryRepository,
    IDataCatalogService catalogService,
    IQuerySqlBuilderFactory sqlBuilderFactory,
    IQueryExecutionService executionService,
    IExportService exportService,
    IDataScopeGuard dataScopeGuard,
    ICurrentDataScope currentScope,
    RecordLimitSettings recordLimitSettings,
    IAuditLogger auditLogger)
    : IRequestHandler<ExportQueryCommand, ExportResult>
{
    public async Task<ExportResult> Handle(ExportQueryCommand request, CancellationToken cancellationToken)
    {
        var dataSource = await dataSourceRepository.GetByIdAsync(request.DataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), request.DataSourceId);

        if (request.SavedQueryId is { } savedQueryId)
        {
            var savedQuery = await savedQueryRepository.GetByIdAsync(savedQueryId, cancellationToken)
                ?? throw new NotFoundException(nameof(SavedQuery), savedQueryId);
            if (savedQuery.IsDisabled)
            {
                throw new QueryDisabledException($"'{savedQuery.Name}' is disabled and cannot be exported.");
            }
        }

        await catalogService.ValidateAsync(request.DataSourceId, request.Definition, cancellationToken);
        var scopePredicates = await dataScopeGuard.AuthorizeAsync(request.DataSourceId, request.Definition, cancellationToken);

        var builder = sqlBuilderFactory.GetBuilder(dataSource.Provider);
        var generated = builder.Build(request.Definition, scopePredicates);

        // With a record limit, an oversized export is refused rather than cut off: a truncated file
        // looks complete to whoever opens it. Without one, the legacy silent 100,000 cap applies.
        var limit = RecordLimits.Effective(dataSource.MaxRecords, recordLimitSettings);
        var result = await executionService.ExecuteAsync(
            dataSource, generated, request.ParameterValues, maxRows: limit ?? RecordLimits.LegacyExportRows, cancellationToken);

        if (limit is not null && result.Truncated)
        {
            throw new RecordLimitExceededException(
                $"This query returns more than {limit.Value:N0} records, the limit for this data source. " +
                "Add filters to narrow it down before exporting.");
        }

        var fileNameSafe = string.IsNullOrWhiteSpace(request.FileName) ? "query-results" : request.FileName;
        var content = await exportService.ExportAsync(result, request.Format, fileNameSafe, cancellationToken);

        var entityName = $"{request.Definition.Source.SchemaName}.{request.Definition.Source.ObjectName}";
        await auditLogger.LogAsync(
            new AuditEntry(
                AuditAction.QueryExported,
                nameof(SavedQuery),
                request.SavedQueryId,
                entityName,
                request.DataSourceId,
                $"Exported '{entityName}' to {request.Format} ({result.RowCount} row(s))",
                new { request.Format, result.RowCount, Scope = (await currentScope.GetAsync(cancellationToken)).Describe() }),
            cancellationToken);

        return new ExportResult(
            content,
            exportService.GetContentType(request.Format),
            $"{fileNameSafe}{exportService.GetFileExtension(request.Format)}");
    }
}
