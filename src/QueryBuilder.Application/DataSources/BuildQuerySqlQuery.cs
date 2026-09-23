using QueryBuilder.Application.Common;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.DataSources;

public sealed record BuildQuerySqlQuery(Guid DataSourceId, QueryDefinition Definition) : IRequest<QuerySqlPreviewDto>;

public sealed class BuildQuerySqlQueryHandler(
    IDataSourceRepository dataSourceRepository,
    IDataCatalogService catalogService,
    IQuerySqlBuilderFactory sqlBuilderFactory,
    IDataScopeGuard dataScopeGuard)
    : IRequestHandler<BuildQuerySqlQuery, QuerySqlPreviewDto>
{
    public async Task<QuerySqlPreviewDto> Handle(BuildQuerySqlQuery request, CancellationToken cancellationToken)
    {
        var dataSource = await dataSourceRepository.GetByIdAsync(request.DataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), request.DataSourceId);

        await catalogService.ValidateAsync(request.DataSourceId, request.Definition, cancellationToken);
        var scopePredicates = await dataScopeGuard.AuthorizeAsync(request.DataSourceId, request.Definition, cancellationToken);

        var builder = sqlBuilderFactory.GetBuilder(dataSource.Provider);
        var generated = builder.Build(request.Definition, scopePredicates);

        return new QuerySqlPreviewDto(
            generated.Sql,
            generated.Parameters
                .Select(p => new QuerySqlPreviewParameterDto(
                    p.Name, p.DataType, p.LiteralValue, p.IsRuntimeParameter, p.RuntimeParameterName))
                .ToList());
    }
}
