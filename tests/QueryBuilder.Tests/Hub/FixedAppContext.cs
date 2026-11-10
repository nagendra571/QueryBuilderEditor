using QueryBuilder.Application.Abstractions;

namespace QueryBuilder.Tests.Hub;

internal sealed class FixedAppContext(Guid appId, bool hubWide = false) : IAppContext
{
    public Guid AppId { get; } = appId;
    public bool IsHubWide { get; } = hubWide;
}
