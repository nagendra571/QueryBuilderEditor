using QueryBuilder.Domain.Model;

namespace QueryBuilder.Application.Parameters;

/// <summary>
/// Turns a definition whose filters are "ask at run time" into one with concrete values. Every path
/// that runs, exports, previews or charts a definition must go through this before building SQL; the
/// SQL builder then binds each resulting literal as a SqlParameter, so a parameter value is never
/// part of the SQL text.
/// </summary>
public interface IParameterBinder
{
    /// <summary>
    /// Returns a copy of <paramref name="definition"/> with every parameterized condition replaced by
    /// its validated value. A parameter with no value and no default is an error when required and
    /// drops its condition when optional. With <paramref name="allowMissing"/> (SQL preview only),
    /// such a condition is left parameterized so the preview can still show it as <c>@name</c>.
    /// Throws <see cref="Exceptions.CatalogValidationException"/> naming every invalid parameter.
    /// </summary>
    Task<QueryDefinition> MaterializeAsync(
        QueryDefinition definition,
        IReadOnlyDictionary<string, string?>? values,
        CancellationToken cancellationToken,
        bool allowMissing = false);
}

/// <summary>Supplies the choices of a query-based list parameter by running its source query as the
/// current caller — never from a cache shared between users.</summary>
public interface IParameterOptionsService
{
    Task<IReadOnlyList<ParameterOption>> GetOptionsAsync(QueryParameter parameter, CancellationToken cancellationToken);
}
