using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Commands.Admin;

public sealed record UpdateRecordLimitCommand(Guid DataSourceId, int? MaxRecords) : IRequest<Unit>;

public sealed class UpdateRecordLimitCommandHandler(
    IDataSourceRepository repository,
    ICurrentUserService currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<UpdateRecordLimitCommand, Unit>
{
    public async Task<Unit> Handle(UpdateRecordLimitCommand request, CancellationToken cancellationToken)
    {
        var dataSource = await repository.GetByIdAsync(request.DataSourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(DataSource), request.DataSourceId);

        if (request.MaxRecords is { } value && !RecordLimits.IsValid(value))
        {
            throw new CatalogValidationException(
                $"The record limit must be between {RecordLimits.Min:N0} and {RecordLimits.Max:N0}, or empty to use the default.");
        }

        var previous = dataSource.MaxRecords;
        dataSource.MaxRecords = request.MaxRecords;
        dataSource.UpdatedBy = currentUser.UserId;
        dataSource.UpdatedAtUtc = DateTimeOffset.UtcNow;
        repository.Update(dataSource);
        await repository.SaveChangesAsync(cancellationToken);

        await auditLogger.LogAsync(
            new AuditEntry(
                AuditAction.DataSourceRecordLimitUpdated,
                nameof(DataSource),
                dataSource.Id,
                dataSource.Name,
                dataSource.Id,
                $"Set the record limit for '{dataSource.Name}' to {Describe(request.MaxRecords)} (was {Describe(previous)})",
                new { Previous = previous, request.MaxRecords }),
            cancellationToken);

        return Unit.Value;
    }

    private static string Describe(int? limit) => limit is { } value ? value.ToString("N0") : "the default";
}
