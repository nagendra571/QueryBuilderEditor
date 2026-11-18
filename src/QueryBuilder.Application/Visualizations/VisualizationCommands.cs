using FluentValidation;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Visualizations;

public static class VisualizationMapper
{
    public const int MaxPerQuery = 20;

    public static VisualizationDto ToDto(QueryVisualization v, AccessActor viewer) => new(
        v.Id, v.SavedQueryId, v.Name, v.Type, VisualizationOptionsSerializer.Deserialize(v.OptionsJson),
        v.DisplayOrder, SuperAdminMask.Actor(v.CreatedBy, viewer), v.CreatedAtUtc, SuperAdminMask.Actor(v.UpdatedBy, viewer), v.UpdatedAtUtc);
}

public sealed record GetVisualizationsQuery(Guid SavedQueryId) : IRequest<List<VisualizationDto>>;

public sealed class GetVisualizationsQueryHandler(
    ISavedQueryRepository savedQueries, IVisualizationRepository visualizations, ICurrentUserService currentUser)
    : IRequestHandler<GetVisualizationsQuery, List<VisualizationDto>>
{
    public async Task<List<VisualizationDto>> Handle(GetVisualizationsQuery request, CancellationToken cancellationToken)
    {
        var query = await savedQueries.GetByIdAsync(request.SavedQueryId, cancellationToken)
            ?? throw new NotFoundException(nameof(SavedQuery), request.SavedQueryId);

        if (!SavedQueryAccess.CanView(query, AccessActor.From(currentUser)))
        {
            throw new ForbiddenException("You do not have access to this query.");
        }

        var items = await visualizations.GetForQueryAsync(query.Id, cancellationToken);
        return items.Select(i => VisualizationMapper.ToDto(i, AccessActor.From(currentUser))).ToList();
    }
}

public sealed record SaveVisualizationCommand(
    Guid SavedQueryId,
    Guid? VisualizationId,
    string Name,
    VisualizationType Type,
    VisualizationOptions Options) : IRequest<VisualizationDto>;

public sealed class SaveVisualizationCommandValidator : AbstractValidator<SaveVisualizationCommand>
{
    public SaveVisualizationCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Options).NotNull();
        RuleFor(x => x.Type).IsInEnum();
    }
}

public sealed class SaveVisualizationCommandHandler(
    ISavedQueryRepository savedQueries,
    IVisualizationRepository visualizations,
    IDataCatalogService catalog,
    ICurrentUserService currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<SaveVisualizationCommand, VisualizationDto>
{
    public async Task<VisualizationDto> Handle(SaveVisualizationCommand request, CancellationToken cancellationToken)
    {
        var query = await savedQueries.GetByIdAsync(request.SavedQueryId, cancellationToken)
            ?? throw new NotFoundException(nameof(SavedQuery), request.SavedQueryId);

        if (!SavedQueryAccess.CanEdit(query, AccessActor.From(currentUser)))
        {
            throw new ForbiddenException("You have view-only access to this query — you cannot change its charts.");
        }

        // The definition is the saved one, never one supplied by the browser.
        var definition = QueryDefinitionSerializer.Deserialize(query.DefinitionJson);
        var issues = VisualizationOptionsValidator.Validate(request.Type, request.Options, definition);
        if (issues.Count > 0)
        {
            throw new CatalogValidationException(string.Join(" ", issues.Select(i => i.Message)));
        }

        await ValidateDrillColumnsAsync(query, definition, request.Options, cancellationToken);

        var optionsJson = VisualizationOptionsSerializer.Serialize(request.Options);
        var name = request.Name.Trim();

        if (request.VisualizationId is { } id && id != Guid.Empty)
        {
            var existing = await visualizations.GetByIdAsync(id, cancellationToken);
            if (existing is null || existing.SavedQueryId != query.Id)
            {
                throw new NotFoundException(nameof(QueryVisualization), id);
            }

            existing.Name = name;
            existing.Type = request.Type;
            existing.OptionsJson = optionsJson;
            existing.UpdatedBy = currentUser.UserId;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

            visualizations.Update(existing);
            await visualizations.SaveChangesAsync(cancellationToken);

            await auditLogger.LogAsync(
                new AuditEntry(AuditAction.VisualizationUpdated, nameof(SavedQuery), query.Id, query.Name, query.DataSourceId,
                    $"Updated chart '{existing.Name}' on query '{query.Name}'", new { VisualizationId = existing.Id }),
                cancellationToken);

            return VisualizationMapper.ToDto(existing, AccessActor.From(currentUser));
        }

        var current = await visualizations.GetForQueryAsync(query.Id, cancellationToken);
        if (current.Count >= VisualizationMapper.MaxPerQuery)
        {
            throw new CatalogValidationException($"A query can have at most {VisualizationMapper.MaxPerQuery} charts.");
        }

        var created = new QueryVisualization
        {
            SavedQueryId = query.Id,
            Name = name,
            Type = request.Type,
            OptionsJson = optionsJson,
            DisplayOrder = current.Count == 0 ? 0 : current.Max(v => v.DisplayOrder) + 1,
            CreatedBy = currentUser.UserId
        };

        // Added through the DbSet, never a parent navigation collection (see SavedQueryRepository.AddShareAsync).
        await visualizations.AddAsync(created, cancellationToken);
        await visualizations.SaveChangesAsync(cancellationToken);

        await auditLogger.LogAsync(
            new AuditEntry(AuditAction.VisualizationCreated, nameof(SavedQuery), query.Id, query.Name, query.DataSourceId,
                $"Created chart '{created.Name}' on query '{query.Name}'", new { VisualizationId = created.Id }),
            cancellationToken);

        return VisualizationMapper.ToDto(created, AccessActor.From(currentUser));
    }

    // Only the source object's own columns are offered when drilling, so a crafted body can't name another object's.
    private async Task ValidateDrillColumnsAsync(
        SavedQuery query, QueryDefinition definition, VisualizationOptions options, CancellationToken cancellationToken)
    {
        if (options.Drill is not { Columns.Count: > 0 } drill)
        {
            return;
        }

        var cat = await catalog.GetCatalogAsync(query.DataSourceId, cancellationToken);
        var comparer = IdentifierMatching.Comparer(cat.Provider);

        var duplicate = drill.Columns.GroupBy(c => c, comparer).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new CatalogValidationException($"'{duplicate.Key}' is chosen more than once to show when drilling.");
        }

        var comparison = IdentifierMatching.Comparison(cat.Provider);
        var source = definition.Source;
        var columns = cat.Schemas
            .SelectMany(s => s.Objects)
            .FirstOrDefault(o => string.Equals(o.SchemaName, source.SchemaName, comparison)
                && string.Equals(o.Name, source.ObjectName, comparison))
            ?.Columns ?? [];

        foreach (var column in drill.Columns)
        {
            if (!columns.Any(c => string.Equals(c.Name, column, comparison)))
            {
                throw new CatalogValidationException($"'{column}' is not a column of {source.SchemaName}.{source.ObjectName}.");
            }
        }
    }
}

public sealed record DeleteVisualizationCommand(Guid SavedQueryId, Guid VisualizationId) : IRequest<Unit>;

public sealed class DeleteVisualizationCommandHandler(
    ISavedQueryRepository savedQueries,
    IVisualizationRepository visualizations,
    ICurrentUserService currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<DeleteVisualizationCommand, Unit>
{
    public async Task<Unit> Handle(DeleteVisualizationCommand request, CancellationToken cancellationToken)
    {
        var query = await savedQueries.GetByIdAsync(request.SavedQueryId, cancellationToken)
            ?? throw new NotFoundException(nameof(SavedQuery), request.SavedQueryId);

        if (!SavedQueryAccess.CanEdit(query, AccessActor.From(currentUser)))
        {
            throw new ForbiddenException("You have view-only access to this query — you cannot change its charts.");
        }

        var existing = await visualizations.GetByIdAsync(request.VisualizationId, cancellationToken);
        if (existing is null || existing.SavedQueryId != query.Id)
        {
            throw new NotFoundException(nameof(QueryVisualization), request.VisualizationId);
        }

        visualizations.Remove(existing);
        await visualizations.SaveChangesAsync(cancellationToken);

        await auditLogger.LogAsync(
            new AuditEntry(AuditAction.VisualizationDeleted, nameof(SavedQuery), query.Id, query.Name, query.DataSourceId,
                $"Deleted chart '{existing.Name}' from query '{query.Name}'", new { VisualizationId = existing.Id }),
            cancellationToken);

        return Unit.Value;
    }
}
