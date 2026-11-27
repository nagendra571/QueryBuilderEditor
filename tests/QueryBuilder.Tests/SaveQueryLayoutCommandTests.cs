using System.Text.Json;
using QueryBuilder.Application.Commands.SavedQueries;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Queries.SavedQueries;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Tests;

public class SaveQueryLayoutCommandTests
{
    private static SaveQueryLayoutCommandHandler Handler(VizFixture f) => new(f.Queries, f.User, f.Audit);

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static SaveQueryLayoutCommand Save(VizFixture f, string raw) => new(f.Query.Id, Json(raw));

    [Theory]
    [InlineData(VizFixture.Owner)]
    [InlineData(VizFixture.EditorUser)]
    public async Task Owner_and_editor_can_save_a_layout(string user)
    {
        var f = new VizFixture();
        f.User.UserId = user;

        var has = await Handler(f).Handle(Save(f, """{"v":1,"wrap":true}"""), default);

        Assert.True(has);
        Assert.Contains("\"v\":1", f.Query.LayoutJson);
        var entry = Assert.Single(f.Audit.Entries);
        Assert.Equal(AuditAction.QueryLayoutSaved, entry.Action);
        Assert.Equal("Saved the results layout of query 'Orders by region'", entry.Summary);
    }

    [Theory]
    [InlineData(VizFixture.ViewerUser)]
    [InlineData(VizFixture.Stranger)]
    public async Task Viewer_and_stranger_are_refused(string user)
    {
        var f = new VizFixture();
        f.User.UserId = user;

        await Assert.ThrowsAsync<ForbiddenException>(() => Handler(f).Handle(Save(f, """{"v":1}"""), default));

        Assert.Null(f.Query.LayoutJson);
        Assert.Empty(f.Audit.Entries);
    }

    [Fact]
    public async Task Unknown_query_is_not_found()
    {
        var f = new VizFixture();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Handler(f).Handle(new SaveQueryLayoutCommand(Guid.NewGuid(), Json("""{"v":1}""")), default));
    }

    [Fact]
    public async Task Null_layout_clears_it()
    {
        var f = new VizFixture();
        f.Query.LayoutJson = """{"v":1}""";

        var has = await Handler(f).Handle(new SaveQueryLayoutCommand(f.Query.Id, null), default);

        Assert.False(has);
        Assert.Null(f.Query.LayoutJson);
        Assert.Equal("Cleared the results layout of query 'Orders by region'", Assert.Single(f.Audit.Entries).Summary);
    }

    [Fact]
    public async Task Json_null_layout_clears_it()
    {
        var f = new VizFixture();
        f.Query.LayoutJson = """{"v":1}""";

        var has = await Handler(f).Handle(Save(f, "null"), default);

        Assert.False(has);
        Assert.Null(f.Query.LayoutJson);
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("\"x\"")]
    [InlineData("{}")]
    [InlineData("""{"v":2}""")]
    [InlineData("""{"v":"1"}""")]
    public async Task Layout_must_be_an_object_with_v_1(string raw)
    {
        var f = new VizFixture();

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Handler(f).Handle(Save(f, raw), default));

        Assert.Equal("The layout must be a JSON object with \"v\": 1.", ex.Message);
        Assert.Null(f.Query.LayoutJson);
        Assert.Empty(f.Audit.Entries);
    }

    [Fact]
    public async Task Layout_v_1_point_0_is_accepted()
    {
        var f = new VizFixture();

        Assert.True(await Handler(f).Handle(Save(f, """{"v":1.0}"""), default));
    }

    [Fact]
    public async Task Too_large_layout_is_refused_and_one_at_the_limit_is_accepted()
    {
        var f = new VizFixture();
        var tooBig = "{\"v\":1,\"p\":\"" + new string('a', 262200) + "\"}";

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => Handler(f).Handle(Save(f, tooBig), default));
        Assert.Equal("The layout is too large.", ex.Message);
        Assert.Null(f.Query.LayoutJson);

        const string head = "{\"v\":1,\"p\":\"";
        const string tail = "\"}";
        var exact = head + new string('a', 262144 - head.Length - tail.Length) + tail;
        Assert.Equal(262144, exact.Length);

        Assert.True(await Handler(f).Handle(Save(f, exact), default));
        Assert.Equal(262144, f.Query.LayoutJson!.Length);
    }

    [Fact]
    public async Task Saving_a_layout_leaves_definition_and_update_stamp_alone()
    {
        var f = new VizFixture();
        f.Query.UpdatedBy = "someone";
        f.Query.UpdatedAtUtc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var (definition, name, at, by) = (f.Query.DefinitionJson, f.Query.Name, f.Query.UpdatedAtUtc, f.Query.UpdatedBy);

        await Handler(f).Handle(Save(f, """{"v":1}"""), default);

        Assert.Equal(definition, f.Query.DefinitionJson);
        Assert.Equal(name, f.Query.Name);
        Assert.Equal(at, f.Query.UpdatedAtUtc);
        Assert.Equal(by, f.Query.UpdatedBy);
    }

    [Fact]
    public async Task Disabled_query_still_saves_layout()
    {
        var f = new VizFixture();
        f.Query.IsDisabled = true;

        Assert.True(await Handler(f).Handle(Save(f, """{"v":1}"""), default));
    }

    [Fact]
    public async Task Detail_query_returns_layout_as_object()
    {
        var f = new VizFixture();
        var handler = new GetSavedQueryByIdQueryHandler(f.Queries, f.User);
        f.Query.LayoutJson = """{"v":1,"wrap":true}""";

        var dto = await handler.Handle(new GetSavedQueryByIdQuery(f.Query.Id), default);

        Assert.Equal(JsonValueKind.Object, dto.Layout!.Value.ValueKind);

        f.Query.LayoutJson = "not json";
        Assert.Null((await handler.Handle(new GetSavedQueryByIdQuery(f.Query.Id), default)).Layout);
    }
}
