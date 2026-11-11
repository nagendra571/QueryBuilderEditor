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

    public static VisualizationDto ToDto(QueryVisualization v) => new(
        v.Id, v.SavedQueryId, v.Name, v.Type, VisualizationOptionsSerializer.Deserialize(v.OptionsJson),
        v.DisplayOrder, v.CreatedBy, v.CreatedAtUtc, v.UpdatedBy, v.UpdatedAtUtc);
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
        return items.Select(VisualizationMapper.ToDto).ToList();
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

            return VisualizationMapper.ToDto(existing);
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

        return VisualizationMapper.ToDto(created);
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
