using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Editor.Hub;

/// <summary>
/// Keeps the Data Protection key ring in the metadata database so every hub instance reads each other's session cookies.
/// Insert-only on purpose: nothing here updates or deletes a key, so a bug or a second instance cannot damage the ring.
/// Keys arrive already encrypted by <see cref="HubKeyEncryptor"/> (keyed from the data source encryption key), so the
/// stored XML never holds a key in clear.
/// </summary>
internal sealed class MetadataXmlRepository(IServiceScopeFactory scopes) : IXmlRepository
{
    // A scope of its own: DataProtectionKeyRecord is a system record, writable whichever app the caller is in.
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.DataProtectionKeys.AsNoTracking().OrderBy(k => k.Id).Select(k => k.Xml).ToList()
            .Select(xml => XElement.Parse(xml)).ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.DataProtectionKeys.Add(new DataProtectionKeyRecord
        {
            FriendlyName = friendlyName.Length > 200 ? friendlyName[..200] : friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting),
        });
        db.SaveChanges();
    }
}
