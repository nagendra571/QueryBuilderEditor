using QueryBuilder.Application.Common;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;

namespace QueryBuilder.Application.Queries.Admin;

public sealed record GetAdminDataSourceDetailQuery(Guid Id) : IRequest<AdminDataSourceDetailDto>;

public sealed class GetAdminDataSourceDetailQueryHandler(
    IDataSourceRepository repository, IDataCatalogService catalogService, RecordLimitSettings recordLimitSettings)
    : IRequestHandler<GetAdminDataSourceDetailQuery, AdminDataSourceDetailDto>
{
    public async Task<AdminDataSourceDetailDto> Handle(GetAdminDataSourceDetailQuery request, CancellationToken cancellationToken)
    {
        var dataSource = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), request.Id);

        var objects = await catalogService.GetRawObjectsAsync(request.Id, cancellationToken);

        return new AdminDataSourceDetailDto(
            dataSource.Id,
            dataSource.Name,
            dataSource.CatalogScope,
            dataSource.AllowedObjects,
            objects,
            dataSource.MaxRecords,
            recordLimitSettings.DefaultMaxRecords);
    }
}
