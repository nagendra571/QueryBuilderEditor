namespace QueryBuilder.Application.Exceptions;

public sealed class CatalogValidationException(string message) : Exception(message);

public sealed class NotFoundException(string entityName, object key)
    : Exception($"{entityName} with id '{key}' was not found.");

public sealed class ForbiddenException(string message) : Exception(message);

public sealed class QueryExecutionException(string message, Exception? inner = null) : Exception(message, inner);
