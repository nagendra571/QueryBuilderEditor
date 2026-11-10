namespace QueryBuilder.Application.Abstractions;

/// <summary>Which app the current request works in. When <see cref="IsHubWide"/> is true, <see cref="AppId"/>
/// is meaningless and the data layer's app filters are off.</summary>
public interface IAppContext
{
    Guid AppId { get; }
    bool IsHubWide { get; }
}
