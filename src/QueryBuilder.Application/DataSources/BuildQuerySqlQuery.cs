using QueryBuilder.Application.Common;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Parameters;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.DataSources;

public sealed record BuildQuerySqlQuery(
    Guid DataSourceId, QueryDefinition Definition, Dictionary<string, string?>? ParameterValues = null) : IRequest<QuerySqlPreviewDto>;

public sealed class BuildQuerySqlQueryHandler(
    IDataSourceRepository dataSourceRepository,
    IDataCatalogService catalogService,
    IQuerySqlBuilderFactory sqlBuilderFactory,
    IDataScopeGuard dataScopeGuard,
    IParameterBinder parameterBinder)
    : IRequestHandler<BuildQuerySqlQuery, QuerySqlPreviewDto>
{
    public async Task<QuerySqlPreviewDto> Handle(BuildQuerySqlQuery request, CancellationToken cancellationToken)
    {
        var dataSource = await dataSourceRepository.GetByIdAsync(request.DataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), request.DataSourceId);

        await catalogService.ValidateAsync(request.DataSourceId, request.Definition, cancellationToken);
        // allowMissing: previewing a query that still asks for a value shows that filter as @name.
        var definition = await parameterBinder.MaterializeAsync(request.Definition, request.ParameterValues, cancellationToken, allowMissing: true);
        var scopePredicates = await dataScopeGuard.AuthorizeAsync(request.DataSourceId, definition, cancellationToken);

        var builder = sqlBuilderFactory.GetBuilder(dataSource.Provider);
        var generated = builder.Build(definition, scopePredicates);

        return new QuerySqlPreviewDto(
            generated.Sql,
            generated.Parameters
                .Select(p => new QuerySqlPreviewParameterDto(
                    p.Name, p.DataType, p.LiteralValue, p.IsRuntimeParameter, p.RuntimeParameterName))
                .ToList());
    }
}
