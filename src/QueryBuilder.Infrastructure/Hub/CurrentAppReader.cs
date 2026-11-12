using Microsoft.EntityFrameworkCore;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Infrastructure.Persistence;

namespace QueryBuilder.Infrastructure.Hub;

internal sealed class CurrentAppReader(AppDbContext db, IAppContext appContext) : ICurrentAppReader
{
    public Task<App?> GetCurrentAsync(CancellationToken ct) =>
        appContext.IsHubWide || appContext.AppId == App.NoneId
            ? Task.FromResult<App?>(null)
            : db.Apps.AsNoTracking().FirstOrDefaultAsync(a => a.Id == appContext.AppId, ct);
}
