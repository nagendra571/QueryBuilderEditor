using QueryBuilder.Application.Common;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Queries.Admin;

public sealed record GetAdminDataSourceDetailQuery(Guid Id) : IRequest<AdminDataSourceDetailDto>;

public sealed class GetAdminDataSourceDetailQueryHandler(
    IDataSourceRepository repository,
    IDataCatalogService catalogService,
    RecordLimitSettings recordLimitSettings,
    IConnectionSecretProtector protector)
    : IRequestHandler<GetAdminDataSourceDetailQuery, AdminDataSourceDetailDto>
{
    public async Task<AdminDataSourceDetailDto> Handle(GetAdminDataSourceDetailQuery request, CancellationToken cancellationToken)
    {
        var dataSource = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), request.Id);

        // An unreachable source must still open: that is the page where its connection gets fixed.
        List<SchemaObjectMetadata> objects = [];
        List<AdminUnavailableObject> unavailable = [];
        string? catalogError = null;
        try
        {
            objects = await catalogService.GetRawObjectsAsync(request.Id, cancellationToken);
            unavailable = await catalogService.GetUnavailableObjectsAsync(request.Id, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Whatever went wrong connecting (a driver error, a missing configuration entry, a bad connection string),
            // the admin still gets the page, with a short reason and no driver text.
            catalogError = ex is QueryExecutionException or InvalidOperationException
                ? ex.Message
                : "Couldn't connect to this data source.";
        }

        var details = ManagedConnections.TryRead(dataSource, protector);
        var usesMetadata = dataSource.ConnectionStringName == DataSource.DefaultConnectionStringSentinel;

        return new AdminDataSourceDetailDto(
            dataSource.Id,
            dataSource.Name,
            dataSource.CatalogScope,
            dataSource.AllowedObjects,
            objects,
            dataSource.MaxRecords,
            recordLimitSettings.DefaultMaxRecords,
            unavailable,
            dataSource.Description,
            dataSource.IsActive,
            dataSource.IsManaged,
            dataSource.Provider,
            details is null ? null : ManagedConnections.ToDto(details),
            dataSource.IsManaged && details is null,
            dataSource.IsManaged || usesMetadata ? null : dataSource.ConnectionStringName,
            catalogError,
            usesMetadata);
    }
}
