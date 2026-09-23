using Microsoft.Extensions.Caching.Memory;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Domain.Model;
using QueryBuilder.Infrastructure.Catalog;

namespace QueryBuilder.Tests;

public class ParameterNameValidationTests
{
    [Theory]
    [InlineData("Region", true)]
    [InlineData("Order_Date_1", true)]
    [InlineData("_id", true)]
    [InlineData("2024Sales", true)]
    [InlineData("__scope0", false)]
    [InlineData("x) OR (1=1", false)]
    [InlineData("a;DROP TABLE x", false)]
    [InlineData("name with space", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid(string? name, bool expected) => Assert.Equal(expected, QueryParameterNames.IsValid(name));

    // GetCatalogAsync is served from the cache, so ValidateAsync never touches a database here.
    private static SqlServerDataCatalogService CatalogService()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set($"catalog:{TestData.DataSourceId}", TestData.Catalog());
        return new SqlServerDataCatalogService(null!, null!, cache);
    }

    private static QueryDefinition ParameterizedDefinition(string parameterName)
    {
        var definition = TestData.Definition();
        definition.Parameters.Add(new QueryParameter { Name = parameterName, DataType = ColumnDataType.Text });
        var condition = TestData.Condition("RegionCode", FilterOperator.Equals, null);
        condition.IsParameterized = true;
        condition.ParameterName = parameterName;
        definition.Filters.Conditions.Add(condition);
        return definition;
    }

    [Fact]
    public async Task Validate_accepts_a_normal_parameter()
    {
        await CatalogService().ValidateAsync(TestData.DataSourceId, ParameterizedDefinition("RegionCode"), default);
    }

    [Theory]
    [InlineData("x) OR (1=1")]
    [InlineData("__scope0")]
    public async Task Validate_rejects_declared_and_referenced_malicious_name(string name)
    {
        await Assert.ThrowsAsync<CatalogValidationException>(() =>
            CatalogService().ValidateAsync(TestData.DataSourceId, ParameterizedDefinition(name), default));
    }
}
