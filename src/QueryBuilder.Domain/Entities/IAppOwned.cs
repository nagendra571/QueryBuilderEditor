namespace QueryBuilder.Domain.Entities;

/// <summary>A metadata row that belongs to exactly one app; the data layer filters and stamps by it.</summary>
public interface IAppOwned
{
    Guid AppId { get; set; }
}
