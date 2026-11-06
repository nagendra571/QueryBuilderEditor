using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Infrastructure.Catalog;

namespace QueryBuilder.Tests.Postgres;

public class AmbiguousObjectsTests
{
    private static SchemaObjectMetadata Obj(string schema, string name, SchemaObjectKind kind = SchemaObjectKind.Table) =>
        new() { SchemaName = schema, Name = name, Kind = kind };

    [Fact]
    public void Case_only_pair_is_unavailable_with_partner_names()
    {
        var (usable, unavailable) = AmbiguousObjects.Partition([Obj("public", "Orders"), Obj("public", "orders"), Obj("public", "Items")]);

        Assert.Equal(["Items"], usable.Select(o => o.Name));
        var orders = Assert.Single(unavailable, u => u.Name == "Orders");
        Assert.Equal(["public.orders"], orders.ClashesWith);
        Assert.Equal(UnavailableReason.NameClash, orders.Reason);
        var lower = Assert.Single(unavailable, u => u.Name == "orders");
        Assert.Equal(["public.Orders"], lower.ClashesWith);
    }

    [Fact]
    public void Dot_clash_is_unavailable()
    {
        var (usable, unavailable) = AmbiguousObjects.Partition([Obj("a.b", "c"), Obj("a", "b.c", SchemaObjectKind.View)]);

        Assert.Empty(usable);
        Assert.Equal(2, unavailable.Count);
        var view = Assert.Single(unavailable, u => u.Kind == SchemaObjectKind.View);
        Assert.Equal(["\"a.b\".\"c\""], view.ClashesWith);
        var table = Assert.Single(unavailable, u => u.Kind == SchemaObjectKind.Table);
        Assert.Equal(["\"a\".\"b.c\""], table.ClashesWith);
    }

    [Fact]
    public void Unique_names_are_usable()
    {
        var (usable, unavailable) = AmbiguousObjects.Partition([Obj("public", "a"), Obj("public", "b"), Obj("other", "a")]);

        Assert.Equal(3, usable.Count);
        Assert.Empty(unavailable);
    }

    [Fact]
    public void Three_way_clash_lists_two_partners_each()
    {
        var (usable, unavailable) = AmbiguousObjects.Partition([Obj("public", "Orders"), Obj("public", "orders"), Obj("public", "ORDERS")]);

        Assert.Empty(usable);
        Assert.Equal(3, unavailable.Count);
        Assert.All(unavailable, u => Assert.Equal(2, u.ClashesWith.Count));
        var upper = Assert.Single(unavailable, u => u.Name == "ORDERS");
        Assert.Equal(["public.Orders", "public.orders"], upper.ClashesWith.OrderBy(n => n, StringComparer.Ordinal));
    }
}
