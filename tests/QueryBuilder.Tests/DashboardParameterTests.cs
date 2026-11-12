using System.Text.Json;
using System.Text.Json.Serialization;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Dashboards;
using QueryBuilder.Application.Dtos;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Tests;

public class DashboardParameterTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static DashboardParameterDto Param(string name, ParameterType type = ParameterType.Text, bool multiple = false,
        List<string>? options = null, string? value = null) =>
        new(name, null, type, options, null, null, null, multiple, value);

    private static ParameterMappingDto Dash(string name) => new(ParameterMappingSource.Dashboard, name, null, null);
    private static ParameterMappingDto Fixed(string value) => new(ParameterMappingSource.Fixed, null, value, null);

    /// <summary>Gives a fixture query a stored definition with these parameters (none of them need filters
    /// for mapping validation; the binder only checks values of parameters that a filter uses).</summary>
    private static void Declare(SavedQuery query, params QueryParameter[] parameters)
    {
        var definition = new QueryDefinition { Source = new QuerySource { ObjectName = "T", Alias = "t" } };
        definition.Parameters.AddRange(parameters);
        foreach (var p in parameters)
        {
            definition.Filters.Conditions.Add(new FilterCondition
            {
                TableAlias = "t", ColumnName = p.Name, DataType = p.DataType, Operator = FilterOperator.Equals,
                IsParameterized = true, ParameterName = p.Name
            });
        }
        query.DefinitionJson = QueryDefinitionSerializer.Serialize(definition);
    }

    private static SaveDashboardTilesCommand Save(DashFixture f, List<SaveDashboardTileRequest> tiles,
        List<DashboardParameterDto>? parameters = null) =>
        new(f.Dashboard.Id, f.Dashboard.Version, tiles, parameters);

    private static List<SaveDashboardTileRequest> Tiles(DashFixture f, string openTileRestrictedFor = "owner",
        Dictionary<string, ParameterMappingDto>? openMappings = null) =>
        f.Dashboard.Tiles.Select(t =>
        {
            var req = DashFixture.Req(t);
            return t.Id == f.TileOpen.Id ? req with { ParameterMappings = openMappings } : req;
        }).ToList();

    // ---- dashboard parameter rules ----

    [Fact]
    public async Task Parameters_and_mappings_round_trip_through_save_and_load()
    {
        var f = new DashFixture();
        Declare(f.QueryOpen, new QueryParameter { Name = "program", Label = "Program", Type = ParameterType.Number, DataType = ColumnDataType.Number });
        var mappings = new Dictionary<string, ParameterMappingDto> { ["program"] = Dash("prog") };

        var detail = await f.SaveTilesHandler().Handle(
            Save(f, Tiles(f, openMappings: mappings), [Param("prog", ParameterType.Number, value: "10")]), default);

        var dashParam = Assert.Single(detail.Parameters!);
        Assert.Equal("prog", dashParam.Name);
        Assert.Equal("10", dashParam.Value);
        var open = detail.Tiles.Single(t => t.Id == f.TileOpen.Id);
        Assert.Equal("prog", open.ParameterMappings!["program"].DashboardParameter);
        Assert.NotNull(f.Dashboard.ParametersJson);
        Assert.NotNull(f.TileOpen.ParameterMappingsJson);
    }

    [Fact]
    public async Task More_than_twenty_parameters_are_refused()
    {
        var f = new DashFixture();
        var many = Enumerable.Range(0, 21).Select(i => Param($"p{i}")).ToList();

        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            f.SaveTilesHandler().Handle(Save(f, Tiles(f), many), default));
        Assert.Null(f.Dashboard.ParametersJson);
    }

    [Theory]
    [InlineData("bad name")]
    [InlineData("__reserved")]
    [InlineData("")]
    [InlineData("semi;colon")]
    public async Task Invalid_parameter_names_are_refused(string name)
    {
        var f = new DashFixture();
        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            f.SaveTilesHandler().Handle(Save(f, Tiles(f), [Param(name)]), default));
    }

    [Fact]
    public async Task Duplicate_parameter_names_are_refused_ignoring_case()
    {
        var f = new DashFixture();
        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            f.SaveTilesHandler().Handle(Save(f, Tiles(f), [Param("Region"), Param("region")]), default));
    }

    [Fact]
    public async Task A_list_parameter_needs_options_and_its_value_must_be_one_of_them()
    {
        var f = new DashFixture();
        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            f.SaveTilesHandler().Handle(Save(f, Tiles(f), [Param("l", ParameterType.List)]), default));
        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            f.SaveTilesHandler().Handle(
                Save(f, Tiles(f), [Param("l", ParameterType.List, options: ["a", "b"], value: "z")]), default));
    }

    [Fact]
    public async Task Omitting_parameters_leaves_the_stored_list_alone_and_an_empty_list_clears_it()
    {
        var f = new DashFixture();
        await f.SaveTilesHandler().Handle(Save(f, Tiles(f), [Param("region")]), default);

        var untouched = await f.SaveTilesHandler().Handle(Save(f, Tiles(f)), default);
        Assert.Single(untouched.Parameters!);

        var cleared = await f.SaveTilesHandler().Handle(Save(f, Tiles(f), []), default);
        Assert.Empty(cleared.Parameters!);
        Assert.Null(f.Dashboard.ParametersJson);
    }

    [Fact]
    public async Task A_query_list_parameter_needs_a_source_query_the_editor_can_view()
    {
        var f = new DashFixture();
        f.User.UserId = DashFixture.EditorUser;
        var fromSecret = Param("p", ParameterType.QueryList) with { OptionsQueryId = f.QuerySecret.Id };

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.SaveTilesHandler().Handle(Save(f, Tiles(f).Where(t => t.Id != f.TileSecret.Id).ToList(), [fromSecret]), default));
        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            f.SaveTilesHandler().Handle(Save(f, Tiles(f).Where(t => t.Id != f.TileSecret.Id).ToList(), [Param("p", ParameterType.QueryList)]), default));
    }

    // ---- mapping rules ----

    [Fact]
    public async Task A_mapping_to_a_parameter_the_query_does_not_have_is_refused()
    {
        var f = new DashFixture();
        Declare(f.QueryOpen, new QueryParameter { Name = "program", Type = ParameterType.Number, DataType = ColumnDataType.Number });
        var mappings = new Dictionary<string, ParameterMappingDto> { ["nope"] = Dash("prog") };

        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            f.SaveTilesHandler().Handle(Save(f, Tiles(f, openMappings: mappings), [Param("prog", ParameterType.Number)]), default));
        Assert.Null(f.TileOpen.ParameterMappingsJson);
    }

    [Fact]
    public async Task A_mapping_to_a_missing_dashboard_parameter_is_refused()
    {
        var f = new DashFixture();
        Declare(f.QueryOpen, new QueryParameter { Name = "program", Type = ParameterType.Number, DataType = ColumnDataType.Number });
        var mappings = new Dictionary<string, ParameterMappingDto> { ["program"] = Dash("ghost") };

        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            f.SaveTilesHandler().Handle(Save(f, Tiles(f, openMappings: mappings), []), default));
    }

    [Theory]
    [InlineData(ParameterType.Text, false)]
    [InlineData(ParameterType.DateRange, false)]
    [InlineData(ParameterType.List, false)]
    [InlineData(ParameterType.Number, true)]
    public async Task A_dashboard_parameter_of_an_incompatible_type_is_refused(ParameterType dashboardType, bool compatible)
    {
        var f = new DashFixture();
        Declare(f.QueryOpen, new QueryParameter { Name = "program", Type = ParameterType.Number, DataType = ColumnDataType.Number });
        var mappings = new Dictionary<string, ParameterMappingDto> { ["program"] = Dash("prog") };
        var dash = dashboardType == ParameterType.List ? Param("prog", dashboardType, options: ["1"]) : Param("prog", dashboardType);

        var save = f.SaveTilesHandler().Handle(Save(f, Tiles(f, openMappings: mappings), [dash]), default);
        if (compatible)
        {
            await save;
        }
        else
        {
            await Assert.ThrowsAsync<CatalogValidationException>(() => save);
        }
    }

    [Fact]
    public async Task A_multi_value_dashboard_parameter_cannot_feed_a_single_value_query_parameter()
    {
        var f = new DashFixture();
        Declare(f.QueryOpen, new QueryParameter
        {
            Name = "program", Type = ParameterType.List, Options = ["10", "20"], DataType = ColumnDataType.Text, AllowMultiple = false
        });
        var mappings = new Dictionary<string, ParameterMappingDto> { ["program"] = Dash("prog") };
        var multi = Param("prog", ParameterType.List, multiple: true, options: ["10", "20"]);

        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            f.SaveTilesHandler().Handle(Save(f, Tiles(f, openMappings: mappings), [multi]), default));
    }

    [Fact]
    public async Task A_fixed_value_the_binder_would_refuse_is_refused_at_save()
    {
        var f = new DashFixture();
        Declare(f.QueryOpen, new QueryParameter { Name = "program", Type = ParameterType.Number, DataType = ColumnDataType.Number });

        var bad = new Dictionary<string, ParameterMappingDto> { ["program"] = Fixed("abc") };
        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            f.SaveTilesHandler().Handle(Save(f, Tiles(f, openMappings: bad)), default));

        var good = new Dictionary<string, ParameterMappingDto> { ["program"] = Fixed("30") };
        var detail = await f.SaveTilesHandler().Handle(Save(f, Tiles(f, openMappings: good)), default);
        Assert.Equal("30", detail.Tiles.Single(t => t.Id == f.TileOpen.Id).ParameterMappings!["program"].Value);
    }

    [Fact]
    public async Task Default_mappings_are_not_stored()
    {
        var f = new DashFixture();
        Declare(f.QueryOpen, new QueryParameter { Name = "program", Type = ParameterType.Number, DataType = ColumnDataType.Number });
        var mappings = new Dictionary<string, ParameterMappingDto>
        {
            ["program"] = new(ParameterMappingSource.Default, null, null, null)
        };

        var detail = await f.SaveTilesHandler().Handle(Save(f, Tiles(f, openMappings: mappings)), default);

        Assert.Null(detail.Tiles.Single(t => t.Id == f.TileOpen.Id).ParameterMappings);
        Assert.Null(f.TileOpen.ParameterMappingsJson);
    }

    [Fact]
    public async Task Tile_input_mapping_keeps_its_label_and_no_value()
    {
        var f = new DashFixture();
        Declare(f.QueryOpen, new QueryParameter { Name = "program", Type = ParameterType.Number, DataType = ColumnDataType.Number });
        var mappings = new Dictionary<string, ParameterMappingDto>
        {
            ["program"] = new(ParameterMappingSource.Tile, "ignored", "ignored", " Which program ")
        };

        var detail = await f.SaveTilesHandler().Handle(Save(f, Tiles(f, openMappings: mappings)), default);

        var stored = detail.Tiles.Single(t => t.Id == f.TileOpen.Id).ParameterMappings!["program"];
        Assert.Equal(ParameterMappingSource.Tile, stored.Source);
        Assert.Equal("Which program", stored.Title);
        Assert.Null(stored.Value);
        Assert.Null(stored.DashboardParameter);
    }

    // ---- restricted tiles ----

    [Fact]
    public async Task Restricted_tile_keeps_its_stored_mappings_and_exposes_none_of_them()
    {
        var f = new DashFixture();
        Declare(f.QuerySecret, new QueryParameter { Name = "dept", Type = ParameterType.Text });
        f.TileSecret.ParameterMappingsJson = DashboardParameterStore.WriteMappings(
            new Dictionary<string, ParameterMappingDto> { ["dept"] = Fixed("HR-secret-value") });
        f.User.UserId = DashFixture.EditorUser;

        // The editor can't view the secret chart, so the browser sends the tile without a chart or mappings
        // (and even a forged mapping must be ignored).
        var tiles = f.Dashboard.Tiles
            .Select(t => DashFixture.ReqAsClientSees(t, t.Id == f.TileSecret.Id))
            .Select(r => r.Id == f.TileSecret.Id ? r with { ParameterMappings = new() { ["dept"] = Fixed("forged") } } : r)
            .ToList();

        var detail = await f.SaveTilesHandler().Handle(Save(f, tiles), default);

        var stored = DashboardParameterStore.ReadMappings(f.TileSecret.ParameterMappingsJson)!;
        Assert.Equal("HR-secret-value", stored["dept"].Value);
        var restricted = detail.Tiles.Single(t => t.Id == f.TileSecret.Id);
        Assert.True(restricted.Restricted);
        Assert.Null(restricted.ParameterMappings);
        Assert.DoesNotContain("HR-secret-value", JsonSerializer.Serialize(detail, Json));
    }

    [Fact]
    public async Task Viewer_without_query_access_receives_no_mapping_or_parameter_value_of_that_tile()
    {
        var f = new DashFixture();
        Declare(f.QuerySecret, new QueryParameter { Name = "dept", Type = ParameterType.Text });
        f.TileSecret.ParameterMappingsJson = DashboardParameterStore.WriteMappings(
            new Dictionary<string, ParameterMappingDto> { ["dept"] = Fixed("HR-secret-value") });
        f.User.UserId = DashFixture.Guest;

        var detail = await new GetDashboardQueryHandler(f.Dashboards, f.User).Handle(new GetDashboardQuery(f.Dashboard.Id), default);

        Assert.DoesNotContain("HR-secret-value", JsonSerializer.Serialize(detail, Json));
        Assert.All(detail.Tiles, t => Assert.Null(t.ParameterMappings));
    }

    [Fact]
    public async Task Deleting_a_dashboard_parameter_also_drops_a_restricted_tiles_reference_to_it()
    {
        var f = new DashFixture();
        f.Dashboard.ParametersJson = DashboardParameterStore.WriteParameters([Param("prog")]);
        f.TileSecret.ParameterMappingsJson = DashboardParameterStore.WriteMappings(new Dictionary<string, ParameterMappingDto>
        {
            ["dept"] = Dash("prog"),
            ["other"] = Fixed("x")
        });
        f.User.UserId = DashFixture.EditorUser;
        var tiles = f.Dashboard.Tiles.Select(t => DashFixture.ReqAsClientSees(t, t.Id == f.TileSecret.Id)).ToList();

        await f.SaveTilesHandler().Handle(Save(f, tiles, []), default);

        var stored = DashboardParameterStore.ReadMappings(f.TileSecret.ParameterMappingsJson)!;
        Assert.False(stored.ContainsKey("dept"));
        Assert.True(stored.ContainsKey("other"));
    }

    // ---- compatibility ----

    [Fact]
    public async Task Existing_tiles_without_mappings_save_and_load_unchanged()
    {
        var f = new DashFixture();

        var detail = await f.SaveTilesHandler().Handle(Save(f, Tiles(f)), default);

        Assert.Empty(detail.Parameters!);
        Assert.All(detail.Tiles, t => Assert.Null(t.ParameterMappings));
    }

    [Fact]
    public async Task Retargeting_a_tile_to_another_chart_starts_with_clean_mappings()
    {
        var f = new DashFixture();
        Declare(f.QueryOpen, new QueryParameter { Name = "program", Type = ParameterType.Number, DataType = ColumnDataType.Number });
        f.TileOpen.ParameterMappingsJson = DashboardParameterStore.WriteMappings(
            new Dictionary<string, ParameterMappingDto> { ["program"] = Fixed("30") });
        f.User.UserId = DashFixture.Owner;
        var tiles = f.Dashboard.Tiles
            .Select(DashFixture.Req)
            .Select(r => r.Id == f.TileOpen.Id ? r with { VisualizationId = f.VizSecret.Id } : r)
            .ToList();

        await f.SaveTilesHandler().Handle(Save(f, tiles), default);

        Assert.Null(f.TileOpen.ParameterMappingsJson);
    }
}
