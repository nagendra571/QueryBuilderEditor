namespace QueryBuilder.Application.Exceptions;

public sealed class CatalogValidationException(string message) : Exception(message);

public sealed class NotFoundException : Exception
{
    public NotFoundException(string entityName, object key) : base($"{entityName} with id '{key}' was not found.") { }

    public NotFoundException(string message) : base(message) { }
}

/// <summary>Managed connection details couldn't be protected or read: no key, a different key, or a blob
/// that was changed or belongs to another row. Messages never carry the key or any plaintext.</summary>
public sealed class ConnectionSecretException(string message) : Exception(message);

public sealed class ForbiddenException(string message) : Exception(message);

public sealed class QueryExecutionException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class QueryDisabledException(string message) : Exception(message);

/// <summary>A query returned more records than its data source's record limit allows for the
/// requested operation (export). The message is shown to the user as-is.</summary>
public sealed class RecordLimitExceededException(string message) : Exception(message);

/// <summary>An optimistic-concurrency check failed: someone else changed the object since it was loaded.</summary>
public sealed class ConcurrencyConflictException(string message) : Exception(message);

/// <summary>A write named a row of another app, or no app at all. Callers treat it as not found.</summary>
public sealed class CrossAppWriteException(string message, Guid? appId = null, string? entityType = null, string? entityId = null, Guid? rowAppId = null) : Exception(message)
{
    // Only identifiers, never row contents: this is what the Warning log may say about a refused write.
    public Guid? AppId { get; } = appId;
    public string? EntityType { get; } = entityType;
    public string? EntityId { get; } = entityId;
    public Guid? RowAppId { get; } = rowAppId;
}
