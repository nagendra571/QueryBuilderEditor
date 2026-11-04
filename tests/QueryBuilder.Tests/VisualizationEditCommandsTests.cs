using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Application.Visualizations;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;

namespace QueryBuilder.Tests;

public class VisualizationEditCommandsTests
{
    private static SaveVisualizationCommandHandler SaveHandler(VizFixture f) =>
        new(f.Queries, f.Visualizations, f.User, f.Audit);

    private static DeleteVisualizationCommandHandler DeleteHandler(VizFixture f) =>
        new(f.Queries, f.Visualizations, f.User, f.Audit);

    private static GetVisualizationsQueryHandler ListHandler(VizFixture f) =>
        new(f.Queries, f.Visualizations, f.User);

    private static SaveVisualizationCommand Create(VizFixture f, VisualizationOptions? options = null) =>
        new(f.Query.Id, null, "Orders by region", VisualizationType.Bar, options ?? VizFixture.BarOptions());

    [Theory]
    [InlineData(VizFixture.Owner)]
    [InlineData(VizFixture.EditorUser)]
    public async Task Owner_and_editor_can_create(string user)
    {
        var f = new VizFixture();
        f.User.UserId = user;

        var dto = await SaveHandler(f).Handle(Create(f), default);

        Assert.Single(f.Visualizations.Items);
        Assert.Equal(user, dto.CreatedBy);
        Assert.Equal(AuditAction.VisualizationCreated, Assert.Single(f.Audit.Entries).Action);
    }

    [Theory]
    [InlineData(VizFixture.ViewerUser)]
    [InlineData(VizFixture.Stranger)]
    public async Task Viewer_and_stranger_cannot_create(string user)
    {
        var f = new VizFixture();
        f.User.UserId = user;

        await Assert.ThrowsAsync<ForbiddenException>(() => SaveHandler(f).Handle(Create(f), default));

        Assert.Empty(f.Visualizations.Items);
        Assert.Empty(f.Audit.Entries);
    }

    [Fact]
    public async Task Twenty_first_visualization_is_refused()
    {
        var f = new VizFixture();
        for (var i = 0; i < 20; i++)
        {
            f.AddVisualization(VisualizationType.Bar, VizFixture.BarOptions());
        }

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() => SaveHandler(f).Handle(Create(f), default));

        Assert.Contains("20", ex.Message);
        Assert.Equal(20, f.Visualizations.Items.Count);
    }

    [Fact]
    public async Task Options_are_validated_against_the_saved_definition()
    {
        var f = new VizFixture();

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() =>
            SaveHandler(f).Handle(Create(f, new VisualizationOptions { X = "Region", Measures = ["Region"] }), default));

        Assert.Contains("not a number column", ex.Message);
    }

    [Fact]
    public async Task New_visualizations_get_increasing_display_order()
    {
        var f = new VizFixture();
        f.AddVisualization(VisualizationType.Bar, VizFixture.BarOptions()).DisplayOrder = 4;

        var dto = await SaveHandler(f).Handle(Create(f), default);

        Assert.Equal(5, dto.DisplayOrder);
    }

    [Fact]
    public async Task Editor_can_update_and_viewer_cannot()
    {
        var f = new VizFixture();
        var viz = f.AddVisualization(VisualizationType.Bar, VizFixture.BarOptions());
        var update = new SaveVisualizationCommand(f.Query.Id, viz.Id, "Renamed", VisualizationType.Line, VizFixture.BarOptions());

        f.User.UserId = VizFixture.ViewerUser;
        await Assert.ThrowsAsync<ForbiddenException>(() => SaveHandler(f).Handle(update, default));
        Assert.Equal("Chart", viz.Name);

        f.User.UserId = VizFixture.EditorUser;
        var dto = await SaveHandler(f).Handle(update, default);
        Assert.Equal("Renamed", dto.Name);
        Assert.Equal(VisualizationType.Line, viz.Type);
        Assert.Equal(VizFixture.EditorUser, viz.UpdatedBy);
        Assert.Equal(AuditAction.VisualizationUpdated, f.Audit.Entries.Last().Action);
    }

    [Fact]
    public async Task Updating_a_visualization_of_another_query_is_not_found()
    {
        var f = new VizFixture();
        var other = f.AddVisualization(VisualizationType.Bar, VizFixture.BarOptions(), queryId: Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() => SaveHandler(f).Handle(
            new SaveVisualizationCommand(f.Query.Id, other.Id, "x", VisualizationType.Bar, VizFixture.BarOptions()), default));
    }

    [Theory]
    [InlineData(VizFixture.Owner, true)]
    [InlineData(VizFixture.EditorUser, true)]
    [InlineData(VizFixture.ViewerUser, false)]
    [InlineData(VizFixture.Stranger, false)]
    public async Task Delete_requires_owner_or_editor(string user, bool allowed)
    {
        var f = new VizFixture();
        var viz = f.AddVisualization(VisualizationType.Bar, VizFixture.BarOptions());
        f.User.UserId = user;
        var command = new DeleteVisualizationCommand(f.Query.Id, viz.Id);

        if (allowed)
        {
            await DeleteHandler(f).Handle(command, default);
            Assert.Empty(f.Visualizations.Items);
            Assert.Equal(AuditAction.VisualizationDeleted, Assert.Single(f.Audit.Entries).Action);
        }
        else
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => DeleteHandler(f).Handle(command, default));
            Assert.Single(f.Visualizations.Items);
        }
    }

    [Fact]
    public async Task Deleting_a_visualization_of_another_query_is_not_found()
    {
        var f = new VizFixture();
        var other = f.AddVisualization(VisualizationType.Bar, VizFixture.BarOptions(), queryId: Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() => DeleteHandler(f).Handle(new DeleteVisualizationCommand(f.Query.Id, other.Id), default));
        Assert.Single(f.Visualizations.Items);
    }

    [Theory]
    [InlineData(VizFixture.Owner, true)]
    [InlineData(VizFixture.ViewerUser, true)]
    [InlineData(VizFixture.Stranger, false)]
    public async Task List_requires_any_access(string user, bool allowed)
    {
        var f = new VizFixture();
        f.AddVisualization(VisualizationType.Bar, VizFixture.BarOptions());
        f.User.UserId = user;

        if (allowed)
        {
            Assert.Single(await ListHandler(f).Handle(new GetVisualizationsQuery(f.Query.Id), default));
        }
        else
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => ListHandler(f).Handle(new GetVisualizationsQuery(f.Query.Id), default));
        }
    }
}
