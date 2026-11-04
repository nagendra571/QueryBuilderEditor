using FluentValidation;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Application.Parameters;

namespace QueryBuilder.Application.Dashboards;

public sealed record CreateDashboardCommand(string Name, string? Description) : IRequest<DashboardDetailDto>;

public sealed class CreateDashboardCommandValidator : AbstractValidator<CreateDashboardCommand>
{
    public CreateDashboardCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DashboardAccess.MaxNameLength);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public sealed class CreateDashboardCommandHandler(
    IDashboardRepository dashboards, ICurrentUserService currentUser, IAuditLogger auditLogger)
    : IRequestHandler<CreateDashboardCommand, DashboardDetailDto>
{
    public async Task<DashboardDetailDto> Handle(CreateDashboardCommand request, CancellationToken cancellationToken)
    {
        var dashboard = new Dashboard
        {
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            OwnerId = currentUser.UserId,
            OwnerName = currentUser.DisplayName,
            CreatedBy = currentUser.UserId,
            GridVersion = 2
        };

        await dashboards.AddAsync(dashboard, cancellationToken);
        await dashboards.SaveChangesAsync(cancellationToken);

        await auditLogger.LogAsync(
            new AuditEntry(AuditAction.DashboardCreated, nameof(Dashboard), dashboard.Id, dashboard.Name, null,
                $"Created dashboard '{dashboard.Name}'"),
            cancellationToken);

        return DashboardMapper.ToDetail(dashboard, currentUser.UserId);
    }
}

public sealed record RenameDashboardCommand(Guid DashboardId, string Name, string? Description, int? Version)
    : IRequest<DashboardDetailDto>;

public sealed class RenameDashboardCommandValidator : AbstractValidator<RenameDashboardCommand>
{
    public RenameDashboardCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DashboardAccess.MaxNameLength);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Version).NotNull();
    }
}

public sealed class RenameDashboardCommandHandler(
    IDashboardRepository dashboards, ICurrentUserService currentUser, IAuditLogger auditLogger)
    : IRequestHandler<RenameDashboardCommand, DashboardDetailDto>
{
    public async Task<DashboardDetailDto> Handle(RenameDashboardCommand request, CancellationToken cancellationToken)
    {
        var dashboard = await dashboards.GetByIdAsync(request.DashboardId, cancellationToken)
            ?? throw new NotFoundException(nameof(Dashboard), request.DashboardId);

        if (!DashboardAccess.CanEdit(dashboard, currentUser.UserId))
        {
            throw new ForbiddenException("You do not have permission to edit this dashboard.");
        }

        if (request.Version != dashboard.Version)
        {
            throw new ConcurrencyConflictException("This dashboard was changed by someone else — reload to see the latest.");
        }

        var name = request.Name.Trim();
        if (name.Length == 0 || name.Length > DashboardAccess.MaxNameLength)
        {
            throw new CatalogValidationException($"A dashboard name must be 1 to {DashboardAccess.MaxNameLength} characters.");
        }

        dashboard.Name = name;
        dashboard.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        Touch(dashboard, currentUser.UserId);

        await dashboards.SaveChangesAsync(cancellationToken);

        await auditLogger.LogAsync(
            new AuditEntry(AuditAction.DashboardUpdated, nameof(Dashboard), dashboard.Id, dashboard.Name, null,
                $"Renamed dashboard '{dashboard.Name}'"),
            cancellationToken);

        return DashboardMapper.ToDetail(dashboard, currentUser.UserId);
    }

    internal static void Touch(Dashboard dashboard, string userId)
    {
        dashboard.Version++;
        dashboard.UpdatedBy = userId;
        dashboard.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}

public sealed record SaveDashboardTilesCommand(
    Guid DashboardId,
    int Version,
    IReadOnlyList<SaveDashboardTileRequest> Tiles,
    IReadOnlyList<DashboardParameterDto>? Parameters = null)
    : IRequest<DashboardDetailDto>;

/// <summary>Replaces the dashboard's whole tile set. Chart targets the actor can't view are never
/// retargeted or revealed: an unchanged tile keeps its stored chart by tile id.</summary>
public sealed class SaveDashboardTilesCommandHandler(
    IDashboardRepository dashboards,
    IVisualizationRepository visualizations,
    ISavedQueryRepository savedQueries,
    ICurrentUserService currentUser,
    IAuditLogger auditLogger,
    IParameterBinder binder)
    : IRequestHandler<SaveDashboardTilesCommand, DashboardDetailDto>
{
    /// <param name="Query">The tile's query when the actor may view it; null for text, emptied and restricted tiles.</param>
    /// <param name="Restricted">An existing chart the actor can't view: its stored mappings are kept as they are.</param>
    private sealed record Planned(
        DashboardTile? Existing, SaveDashboardTileRequest Request, Guid? VisualizationId, string? Title, string? Text,
        SavedQuery? Query = null, bool Restricted = false, string? MappingsJson = null);

    public async Task<DashboardDetailDto> Handle(SaveDashboardTilesCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var dashboard = await dashboards.GetByIdAsync(request.DashboardId, cancellationToken)
            ?? throw new NotFoundException(nameof(Dashboard), request.DashboardId);

        if (!DashboardAccess.CanEdit(dashboard, userId))
        {
            throw new ForbiddenException("You do not have permission to edit this dashboard.");
        }

        if (request.Version != dashboard.Version)
        {
            throw new ConcurrencyConflictException("This dashboard was changed by someone else — reload to see the latest.");
        }

        ValidateShape(request.Tiles);

        var storedParameters = DashboardParameterStore.ReadParameters(dashboard.ParametersJson);
        var parameters = request.Parameters is null
            ? storedParameters
            : DashboardParameterValidator.NormalizeParameters(request.Parameters);
        if (request.Parameters is not null)
        {
            await EnsureCanUseOptionQueries(parameters, storedParameters, userId, cancellationToken);
        }

        var existingById = dashboard.Tiles.ToDictionary(t => t.Id);
        var planned = new List<Planned>();
        foreach (var tile in request.Tiles
                     .Select((t, index) => (Tile: t, Index: index))
                     .OrderBy(x => x.Tile.Order).ThenBy(x => x.Index)
                     .Select(x => x.Tile))
        {
            DashboardTile? existing = null;
            if (tile.Id is { } id && !existingById.TryGetValue(id, out existing))
            {
                existing = null;
            }

            planned.Add(tile.Kind switch
            {
                DashboardTileKind.Text => new Planned(existing, tile, null, Clean(tile.Title), tile.Text!.Trim()),
                DashboardTileKind.Section => new Planned(existing, tile, null, tile.Title!.Trim(), Clean(tile.Text)),
                _ => await PlanChart(existing, tile, userId, cancellationToken)
            });
        }

        for (var i = 0; i < planned.Count; i++)
        {
            planned[i] = planned[i] with { MappingsJson = await PlanMappings(planned[i], parameters, cancellationToken) };
        }

        // Nothing above mutated state, so a refusal leaves the dashboard untouched.
        var keptIds = planned.Where(p => p.Existing is not null).Select(p => p.Existing!.Id).ToHashSet();
        foreach (var removed in dashboard.Tiles.Where(t => !keptIds.Contains(t.Id)).ToList())
        {
            dashboards.RemoveTile(removed);
        }

        // Whatever positions the client sent, overlaps and gaps are repaired here; Order follows the result.
        var compacted = DashboardLayout.Compact(planned.Select(p =>
            (p, new LayoutBox(p.Request.X, p.Request.Y, p.Request.Width, p.Request.Height))));

        dashboard.GridVersion = 2;
        if (request.Parameters is not null)
        {
            dashboard.ParametersJson = DashboardParameterStore.WriteParameters(parameters);
        }
        for (var i = 0; i < compacted.Count; i++)
        {
            var (p, box) = compacted[i];
            var tile = p.Existing;
            var isNew = tile is null;
            tile ??= new DashboardTile { DashboardId = dashboard.Id, CreatedBy = userId };

            tile.Kind = p.Request.Kind;
            tile.VisualizationId = p.VisualizationId;
            tile.Title = p.Title;
            tile.Text = p.Text;
            tile.Order = i;
            tile.X = box.X;
            tile.Y = box.Y;
            tile.Width = box.W;
            tile.Height = box.H;
            tile.AutoHeight = p.Request.AutoHeight && p.Request.Kind != DashboardTileKind.Section;
            tile.ParameterMappingsJson = p.MappingsJson;

            if (isNew)
            {
                await dashboards.AddTileAsync(tile, cancellationToken);
            }
            else
            {
                tile.UpdatedBy = userId;
                tile.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        RenameDashboardCommandHandler.Touch(dashboard, userId);
        await dashboards.SaveChangesAsync(cancellationToken);

        await auditLogger.LogAsync(
            new AuditEntry(AuditAction.DashboardUpdated, nameof(Dashboard), dashboard.Id, dashboard.Name, null,
                $"Updated the layout of dashboard '{dashboard.Name}' ({planned.Count} tiles)"),
            cancellationToken);

        var saved = await dashboards.GetByIdAsync(dashboard.Id, cancellationToken) ?? dashboard;
        return DashboardMapper.ToDetail(saved, userId);
    }

    private async Task<Planned> PlanChart(DashboardTile? existing, SaveDashboardTileRequest tile, string userId, CancellationToken ct)
    {
        if (existing is { Kind: DashboardTileKind.Chart, VisualizationId: not null }
            && !CanViewTarget(existing, userId))
        {
            return new Planned(existing, tile, existing.VisualizationId, existing.Title, null, Restricted: true);
        }

        var title = Clean(tile.Title);
        if (tile.VisualizationId is not { } vizId || vizId == Guid.Empty)
        {
            // Only a tile that was already emptied (its chart deleted) may stay chartless.
            if (existing is { Kind: DashboardTileKind.Chart, VisualizationId: null })
            {
                return new Planned(existing, tile, null, title, null);
            }

            throw new CatalogValidationException("A chart tile needs a chart.");
        }

        if (existing is { Kind: DashboardTileKind.Chart } && existing.VisualizationId == vizId)
        {
            return new Planned(existing, tile, vizId, title, null, existing.Visualization?.SavedQuery);
        }

        var viz = await visualizations.GetByIdAsync(vizId, ct)
            ?? throw new NotFoundException(nameof(QueryVisualization), vizId);
        var query = await savedQueries.GetByIdAsync(viz.SavedQueryId, ct)
            ?? throw new NotFoundException(nameof(SavedQuery), viz.SavedQueryId);

        if (!SavedQueryAccess.CanView(query, userId))
        {
            throw new ForbiddenException("You do not have access to the query behind that chart.");
        }

        return new Planned(existing, tile, vizId, title, null, query);
    }

    private static bool DashboardParameterExists(IReadOnlyList<DashboardParameterDto> parameters, ParameterMappingDto mapping) =>
        mapping.Source != ParameterMappingSource.Dashboard
        || parameters.Any(d => string.Equals(d.Name, mapping.DashboardParameter, StringComparison.OrdinalIgnoreCase));

    /// <summary>What to store in the tile's mappings column. A restricted tile keeps its stored value
    /// (minus references to deleted dashboard parameters); a chart whose target changed starts clean
    /// unless the browser sent mappings for the new query.</summary>
    private async Task<string?> PlanMappings(Planned p, IReadOnlyList<DashboardParameterDto> parameters, CancellationToken ct)
    {
        if (p.Request.Kind != DashboardTileKind.Chart)
        {
            return null;
        }

        if (p.Restricted)
        {
            var stored = DashboardParameterStore.ReadMappings(p.Existing?.ParameterMappingsJson);
            return DashboardParameterStore.WriteMappings(
                stored?.Where(kv => DashboardParameterExists(parameters, kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value));
        }

        var incoming = p.Request.ParameterMappings;
        if (incoming is null)
        {
            var sameTarget = p.Existing is { Kind: DashboardTileKind.Chart } && p.Existing.VisualizationId == p.VisualizationId;
            if (!sameTarget)
            {
                return null;
            }
            // Unchanged tile from a client that sends no mappings: keep what is stored, minus dangling references.
            incoming = DashboardParameterStore.ReadMappings(p.Existing!.ParameterMappingsJson)
                ?.Where(kv => DashboardParameterExists(parameters, kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            if (incoming is null)
            {
                return null;
            }
        }

        if (incoming.Count == 0)
        {
            return null;
        }
        if (p.Query is null)
        {
            throw new CatalogValidationException("Parameter mappings need a chart.");
        }

        var definition = QueryDefinitionSerializer.Deserialize(p.Query.DefinitionJson);
        var cleaned = DashboardParameterValidator.NormalizeMappings(incoming, definition.Parameters, parameters);
        await ValidateFixedValues(p.Query.Name, definition, cleaned, ct);
        return DashboardParameterStore.WriteMappings(cleaned);
    }

    /// <summary>Fixed values go through the same binder every run uses, so a value that would be refused
    /// at render time is refused here instead of breaking the tile for every viewer.</summary>
    private async Task ValidateFixedValues(
        string queryName, QueryDefinition definition, Dictionary<string, ParameterMappingDto> mappings, CancellationToken ct)
    {
        var fixedValues = mappings
            .Where(kv => kv.Value.Source == ParameterMappingSource.Fixed)
            .ToDictionary(kv => kv.Key, kv => (string?)kv.Value.Value, StringComparer.Ordinal);
        if (fixedValues.Count == 0)
        {
            return;
        }

        try
        {
            await binder.MaterializeAsync(definition, fixedValues, ct, allowMissing: true);
        }
        catch (CatalogValidationException ex)
        {
            throw new CatalogValidationException($"Fixed value for '{queryName}': {ex.Message}");
        }
    }

    /// <summary>A new or changed source query for a dashboard dropdown must be one the editor can see.</summary>
    private async Task EnsureCanUseOptionQueries(
        IReadOnlyList<DashboardParameterDto> parameters, IReadOnlyList<DashboardParameterDto> stored, string userId, CancellationToken ct)
    {
        foreach (var parameter in parameters.Where(p => p.OptionsQueryId is not null))
        {
            var unchanged = stored.Any(s => string.Equals(s.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)
                                            && s.OptionsQueryId == parameter.OptionsQueryId);
            if (unchanged)
            {
                continue;
            }

            var source = await savedQueries.GetByIdAsync(parameter.OptionsQueryId!.Value, ct);
            if (source is null || !SavedQueryAccess.CanView(source, userId))
            {
                throw new ForbiddenException("You do not have access to the query that supplies a parameter's choices.");
            }
        }
    }

    private static bool CanViewTarget(DashboardTile existing, string userId) =>
        existing.Visualization?.SavedQuery is { } q && SavedQueryAccess.CanView(q, userId);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateSection(SaveDashboardTileRequest tile)
    {
        if (string.IsNullOrWhiteSpace(tile.Title) || tile.Title.Trim().Length > DashboardAccess.MaxSectionTitleLength)
        {
            throw new CatalogValidationException($"A section needs a title of 1 to {DashboardAccess.MaxSectionTitleLength} characters.");
        }

        if (tile.Text is { } description
            && (description.Trim().Length > DashboardAccess.MaxSectionDescriptionLength || description.AsSpan().IndexOfAny('\r', '\n') >= 0))
        {
            throw new CatalogValidationException(
                $"A section description must be one line of at most {DashboardAccess.MaxSectionDescriptionLength} characters.");
        }

        if (tile.VisualizationId is not null)
        {
            throw new CatalogValidationException("A section cannot show a chart.");
        }

        if (tile.Width != DashboardAccess.GridColumns || tile.X != 0 || tile.Height != DashboardAccess.SectionHeight)
        {
            throw new CatalogValidationException(
                $"A section is always {DashboardAccess.GridColumns} columns wide, starts at column 0 and is {DashboardAccess.SectionHeight} row high.");
        }

        if (tile.Y < 0 || tile.Y > DashboardAccess.MaxY)
        {
            throw new CatalogValidationException($"A tile row must be 0 to {DashboardAccess.MaxY}.");
        }
    }

    private static void ValidateShape(IReadOnlyList<SaveDashboardTileRequest> tiles)
    {
        if (tiles.Count > DashboardAccess.MaxTiles)
        {
            throw new CatalogValidationException($"A dashboard can have at most {DashboardAccess.MaxTiles} tiles.");
        }

        var seen = new HashSet<Guid>();
        foreach (var tile in tiles)
        {
            if (!Enum.IsDefined(tile.Kind))
            {
                throw new CatalogValidationException("Unknown tile type.");
            }

            if (tile.Kind == DashboardTileKind.Section)
            {
                ValidateSection(tile);
                if (tile.Id is { } sectionId && !seen.Add(sectionId))
                {
                    throw new CatalogValidationException("A tile appears twice in the request.");
                }

                continue;
            }

            if (tile.Width < DashboardAccess.MinWidth || tile.Width > DashboardAccess.GridColumns)
            {
                throw new CatalogValidationException($"A tile width must be {DashboardAccess.MinWidth} to {DashboardAccess.GridColumns} columns.");
            }

            if (tile.X < 0 || tile.X + tile.Width > DashboardAccess.GridColumns)
            {
                throw new CatalogValidationException($"A tile must sit inside the {DashboardAccess.GridColumns}-column grid.");
            }

            if (tile.Y < 0 || tile.Y > DashboardAccess.MaxY)
            {
                throw new CatalogValidationException($"A tile row must be 0 to {DashboardAccess.MaxY}.");
            }

            if (tile.Height < DashboardAccess.MinHeight || tile.Height > DashboardAccess.MaxHeight)
            {
                throw new CatalogValidationException($"A tile height must be {DashboardAccess.MinHeight} to {DashboardAccess.MaxHeight} rows.");
            }

            if (tile.Title is { Length: > DashboardAccess.MaxNameLength })
            {
                throw new CatalogValidationException($"A tile title can be at most {DashboardAccess.MaxNameLength} characters.");
            }

            if (tile.Kind == DashboardTileKind.Text
                && (string.IsNullOrWhiteSpace(tile.Text) || tile.Text.Length > DashboardAccess.MaxTextLength))
            {
                throw new CatalogValidationException($"A text tile must have 1 to {DashboardAccess.MaxTextLength} characters.");
            }

            if (tile.Id is { } id && !seen.Add(id))
            {
                throw new CatalogValidationException("A tile appears twice in the request.");
            }
        }
    }
}

public sealed record AddChartToDashboardCommand(Guid DashboardId, Guid VisualizationId) : IRequest<DashboardSummaryDto>;

/// <summary>Appends one chart tile from the query page. Needs no version from the browser: appending
/// can't overwrite anyone's layout, and the version bump still makes a stale edit session reload.</summary>
public sealed class AddChartToDashboardCommandHandler(
    IDashboardRepository dashboards,
    IVisualizationRepository visualizations,
    ISavedQueryRepository savedQueries,
    ICurrentUserService currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<AddChartToDashboardCommand, DashboardSummaryDto>
{
    public async Task<DashboardSummaryDto> Handle(AddChartToDashboardCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var dashboard = await dashboards.GetByIdAsync(request.DashboardId, cancellationToken)
            ?? throw new NotFoundException(nameof(Dashboard), request.DashboardId);

        if (!DashboardAccess.CanEdit(dashboard, userId))
        {
            throw new ForbiddenException("You do not have permission to edit this dashboard.");
        }

        var viz = await visualizations.GetByIdAsync(request.VisualizationId, cancellationToken)
            ?? throw new NotFoundException(nameof(QueryVisualization), request.VisualizationId);
        var query = await savedQueries.GetByIdAsync(viz.SavedQueryId, cancellationToken)
            ?? throw new NotFoundException(nameof(SavedQuery), viz.SavedQueryId);

        if (!SavedQueryAccess.CanView(query, userId))
        {
            throw new ForbiddenException("You do not have access to the query behind that chart.");
        }

        if (dashboard.Tiles.Count >= DashboardAccess.MaxTiles)
        {
            throw new CatalogValidationException($"A dashboard can have at most {DashboardAccess.MaxTiles} tiles.");
        }

        // A legacy dashboard is converted for good here: positions must exist before anything is placed below them.
        var boxes = DashboardMapper.ResolveBoxes(dashboard);
        if (dashboard.GridVersion <= 1)
        {
            foreach (var (existing, box) in boxes)
            {
                existing.X = box.X;
                existing.Y = box.Y;
                existing.Width = box.W;
                existing.Height = box.H;
            }

            dashboard.GridVersion = 2;
        }

        var tile = new DashboardTile
        {
            DashboardId = dashboard.Id,
            Kind = DashboardTileKind.Chart,
            VisualizationId = viz.Id,
            Order = dashboard.Tiles.Count == 0 ? 0 : dashboard.Tiles.Max(t => t.Order) + 1,
            X = 0,
            Y = DashboardLayout.BottomY(boxes.Select(b => b.Box)),
            Width = DashboardAccess.DefaultChartWidth,
            Height = DashboardAccess.DefaultChartHeight,
            CreatedBy = userId
        };
        await dashboards.AddTileAsync(tile, cancellationToken);

        RenameDashboardCommandHandler.Touch(dashboard, userId);
        await dashboards.SaveChangesAsync(cancellationToken);

        await auditLogger.LogAsync(
            new AuditEntry(AuditAction.DashboardUpdated, nameof(Dashboard), dashboard.Id, dashboard.Name, null,
                $"Added chart '{viz.Name}' to dashboard '{dashboard.Name}'"),
            cancellationToken);

        var saved = await dashboards.GetByIdAsync(dashboard.Id, cancellationToken) ?? dashboard;
        return DashboardMapper.ToSummary(saved, userId);
    }
}

public sealed record DeleteDashboardCommand(Guid DashboardId) : IRequest<Unit>;

public sealed class DeleteDashboardCommandHandler(
    IDashboardRepository dashboards, ICurrentUserService currentUser, IAuditLogger auditLogger)
    : IRequestHandler<DeleteDashboardCommand, Unit>
{
    public async Task<Unit> Handle(DeleteDashboardCommand request, CancellationToken cancellationToken)
    {
        var dashboard = await dashboards.GetByIdAsync(request.DashboardId, cancellationToken)
            ?? throw new NotFoundException(nameof(Dashboard), request.DashboardId);

        if (!DashboardAccess.IsOwner(dashboard, currentUser.UserId))
        {
            throw new ForbiddenException("Only the owner can delete this dashboard.");
        }

        dashboards.Remove(dashboard);
        await dashboards.SaveChangesAsync(cancellationToken);

        await auditLogger.LogAsync(
            new AuditEntry(AuditAction.DashboardDeleted, nameof(Dashboard), dashboard.Id, dashboard.Name, null,
                $"Deleted dashboard '{dashboard.Name}'"),
            cancellationToken);

        return Unit.Value;
    }
}
