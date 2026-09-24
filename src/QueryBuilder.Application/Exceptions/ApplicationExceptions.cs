namespace QueryBuilder.Application.Exceptions;

public sealed class CatalogValidationException(string message) : Exception(message);

public sealed class NotFoundException(string entityName, object key)
    : Exception($"{entityName} with id '{key}' was not found.");

public sealed class ForbiddenException(string message) : Exception(message);

public sealed class QueryExecutionException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class QueryDisabledException(string message) : Exception(message);

/// <summary>A query returned more records than its data source's record limit allows for the
/// requested operation (export). The message is shown to the user as-is.</summary>
public sealed class RecordLimitExceededException(string message) : Exception(message);
