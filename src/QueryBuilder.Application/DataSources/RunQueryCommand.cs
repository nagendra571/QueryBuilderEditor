using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.DataSources;

public sealed record RunQueryCommand(
    Guid DataSourceId,
    QueryDefinition Definition,
    Dictionary<string, string?> ParameterValues,
    int? MaxRows,
    Guid? SavedQueryId) : IRequest<QueryResultDto>;

public sealed class RunQueryCommandHandler(
    IDataSourceRepository dataSourceRepository,
    ISavedQueryRepository savedQueryRepository,
    IDataCatalogService catalogService,
    IQuerySqlBuilderFactory sqlBuilderFactory,
    IQueryExecutionService executionService,
    IAuditLogger auditLogger)
    : IRequestHandler<RunQueryCommand, QueryResultDto>
{
    public async Task<QueryResultDto> Handle(RunQueryCommand request, CancellationToken cancellationToken)
    {
        var dataSource = await dataSourceRepository.GetByIdAsync(request.DataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), request.DataSourceId);

        if (request.SavedQueryId is { } savedQueryId)
        {
            var savedQuery = await savedQueryRepository.GetByIdAsync(savedQueryId, cancellationToken)
                ?? throw new NotFoundException(nameof(SavedQuery), savedQueryId);
            if (savedQuery.IsDisabled)
            {
                throw new QueryDisabledException($"'{savedQuery.Name}' is disabled and cannot be run.");
            }
        }

        await catalogService.ValidateAsync(request.DataSourceId, request.Definition, cancellationToken);

        var builder = sqlBuilderFactory.GetBuilder(dataSource.Provider);
        var generated = builder.Build(request.Definition);

        var maxRows = request.MaxRows is > 0 and <= 10_000 ? request.MaxRows : 1000;

        var result = await executionService.ExecuteAsync(
            dataSource, generated, request.ParameterValues, maxRows, cancellationToken);

        var entityName = $"{request.Definition.Source.SchemaName}.{request.Definition.Source.ObjectName}";
        await auditLogger.LogAsync(
            new AuditEntry(
                AuditAction.QueryRun,
                nameof(SavedQuery),
                request.SavedQueryId,
                entityName,
                request.DataSourceId,
                $"Ran query against '{entityName}', returned {result.RowCount} row(s) in {result.ExecutionTimeMs}ms",
                new { result.RowCount, result.ExecutionTimeMs, result.Truncated }),
            cancellationToken);

        return new QueryResultDto(
            result.Columns.Select(c => new QueryResultColumnDto(c.Name, c.DataType)).ToList(),
            result.Rows,
            result.RowCount,
            result.ExecutionTimeMs,
            result.Truncated);
    }
}
