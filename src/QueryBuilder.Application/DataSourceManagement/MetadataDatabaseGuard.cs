using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.DataSourceManagement;

public static class MetadataDatabaseGuard
{
    public static async Task RefuseAsync(IMetadataDatabaseIdentity identity, DataSourceProvider provider, ManagedConnectionDetails details, CancellationToken ct)
    {
        if (await identity.IsSameAsync(provider, details, ct))
        {
            throw new CatalogValidationException(MetadataDatabaseMessages.Refused);
        }
    }
}
