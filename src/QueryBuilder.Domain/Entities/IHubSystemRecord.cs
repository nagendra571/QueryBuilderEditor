namespace QueryBuilder.Domain.Entities;

/// <summary>Hub plumbing (launch replay ids, Data Protection keys): never tenant data and never exposed by an endpoint, so any context may write it.</summary>
public interface IHubSystemRecord;
