using System.Text.Json;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.Commands.SavedQueries;

public sealed record SaveQueryLayoutCommand(Guid Id, JsonElement? Layout) : IRequest<bool>;

public sealed class SaveQueryLayoutCommandHandler(
    ISavedQueryRepository repository, ICurrentUserService currentUser, IAuditLogger auditLogger)
    : IRequestHandler<SaveQueryLayoutCommand, bool>
{
    private const int MaxLayoutLength = 262144;

    public async Task<bool> Handle(SaveQueryLayoutCommand request, CancellationToken cancellationToken)
    {
        var existing = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(SavedQuery), request.Id);

        if (!SavedQueryAccess.CanEdit(existing, AccessActor.From(currentUser)))
        {
            throw new ForbiddenException("You have view-only access to this query.");
        }

        string? json = null;
        if (request.Layout is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } layout)
        {
            if (layout.ValueKind != JsonValueKind.Object
                || !layout.TryGetProperty("v", out var v)
                || v.ValueKind != JsonValueKind.Number
                || !v.TryGetDouble(out var version)
                || version != 1)
            {
                throw new CatalogValidationException("The layout must be a JSON object with \"v\": 1.");
            }

            json = layout.GetRawText();
            if (json.Length > MaxLayoutLength)
            {
                throw new CatalogValidationException("The layout is too large.");
            }
        }

        // Not stamping UpdatedAtUtc/UpdatedBy: the layout is a view setting, not an edit of the query.
        existing.LayoutJson = json;
        repository.Update(existing);
        await repository.SaveChangesAsync(cancellationToken);

        var hasLayout = json is not null;
        await auditLogger.LogAsync(
            new AuditEntry(
                AuditAction.QueryLayoutSaved,
                nameof(SavedQuery),
                existing.Id,
                existing.Name,
                existing.DataSourceId,
                hasLayout
                    ? $"Saved the results layout of query '{existing.Name}'"
                    : $"Cleared the results layout of query '{existing.Name}'"),
            cancellationToken);

        return hasLayout;
    }
}
