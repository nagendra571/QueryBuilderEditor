using FluentValidation;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Parameters;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Commands.SavedQueries;

public sealed record SaveQueryCommand(
    Guid? Id,
    string Name,
    string? Description,
    Guid DataSourceId,
    QueryDefinition Definition) : IRequest<Guid>;

public sealed class SaveQueryCommandValidator : AbstractValidator<SaveQueryCommand>
{
    public SaveQueryCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DataSourceId).NotEmpty();
        RuleFor(x => x.Definition).NotNull()
            .WithMessage("The query definition is required.");
        RuleFor(x => x.Definition.Source).NotNull().When(x => x.Definition is not null)
            .WithMessage("Select a table or view to build the query from.");
        RuleFor(x => x.Definition.Source.ObjectName).NotEmpty()
            .When(x => x.Definition?.Source is not null)
            .WithMessage("Select a table or view to build the query from.");
        RuleFor(x => x.Definition.Columns).NotEmpty()
            .When(x => x.Definition is not null)
            .WithMessage("Select at least one column for the query.");
    }
}

public sealed class SaveQueryCommandHandler(
    ISavedQueryRepository repository,
    IDataCatalogService catalogService,
    ICurrentUserService currentUser,
    IDataScopeGuard dataScopeGuard,
    IAuditLogger auditLogger)
    : IRequestHandler<SaveQueryCommand, Guid>
{
    public async Task<Guid> Handle(SaveQueryCommand request, CancellationToken cancellationToken)
    {
        await catalogService.ValidateAsync(request.DataSourceId, request.Definition, cancellationToken);
        await dataScopeGuard.AuthorizeAsync(request.DataSourceId, request.Definition, cancellationToken);
        await ValidateParametersAsync(request, cancellationToken);

        var definitionJson = QueryDefinitionSerializer.Serialize(request.Definition);

        if (request.Id is { } id && id != Guid.Empty)
        {
            var existing = await repository.GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(SavedQuery), id);

            var isOwner = existing.OwnerId == currentUser.UserId;
            var canEdit = existing.Shares.Any(s => s.SharedWithUserId == currentUser.UserId && s.AccessLevel == QueryAccessLevel.Editor);
            if (!isOwner && !canEdit)
            {
                throw new ForbiddenException("You have view-only access to this query — save it as a new query instead.");
            }

            existing.Name = request.Name;
            existing.Description = request.Description;
            existing.DataSourceId = request.DataSourceId;
            existing.DefinitionJson = definitionJson;
            existing.UpdatedBy = currentUser.UserId;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

            repository.Update(existing);
            await repository.SaveChangesAsync(cancellationToken);

            await auditLogger.LogAsync(
                new AuditEntry(AuditAction.QueryUpdated, nameof(SavedQuery), existing.Id, existing.Name, existing.DataSourceId,
                    $"Updated query '{existing.Name}'"),
                cancellationToken);

            return existing.Id;
        }

        var savedQuery = new SavedQuery
        {
            Name = request.Name,
            Description = request.Description,
            DataSourceId = request.DataSourceId,
            DefinitionJson = definitionJson,
            OwnerId = currentUser.UserId,
            OwnerName = currentUser.DisplayName,
            CreatedBy = currentUser.UserId
        };

        await repository.AddAsync(savedQuery, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        await auditLogger.LogAsync(
            new AuditEntry(AuditAction.QueryCreated, nameof(SavedQuery), savedQuery.Id, savedQuery.Name, savedQuery.DataSourceId,
                $"Created query '{savedQuery.Name}'"),
            cancellationToken);

        return savedQuery.Id;
    }

    private async Task ValidateParametersAsync(SaveQueryCommand request, CancellationToken cancellationToken)
    {
        var errors = ParameterValidator.Validate(request.Definition).ToList();

        // A saver can only point a parameter at a query they may view themselves; otherwise saving would
        // be a way to name (and later read through) queries that were never shared with them.
        foreach (var parameter in request.Definition.Parameters.Where(p => ParameterTypes.Effective(p) == ParameterType.QueryList && p.OptionsQueryId is { } id && id != Guid.Empty))
        {
            var sourceId = parameter.OptionsQueryId!.Value;
            var source = sourceId == request.Id ? null : await repository.GetByIdAsync(sourceId, cancellationToken);
            if (source is null || !SavedQueryAccess.CanView(source, currentUser.UserId))
            {
                errors.Add($"{ParameterTypes.Title(parameter)}: the source query is not available to you.");
            }
        }

        if (errors.Count > 0)
        {
            throw new CatalogValidationException(string.Join(" ", errors));
        }
    }
}
