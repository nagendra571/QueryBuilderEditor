using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using QueryBuilder.Application.Exceptions;

namespace QueryBuilder.Editor.Middleware;

/// <summary>
/// Maps domain/application exceptions to clean ProblemDetails responses so the UI can render
/// human-readable error banners instead of raw database/stack-trace output.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title, errors) = Map(exception);

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception processing {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = statusCode == StatusCodes.Status500InternalServerError ? "An unexpected error occurred." : exception.Message,
            Instance = httpContext.Request.Path
        };

        if (errors is not null)
        {
            problemDetails.Extensions["errors"] = errors;
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }

    private static (int StatusCode, string Title, object? Errors) Map(Exception exception) => exception switch
    {
        NotFoundException => (StatusCodes.Status404NotFound, "Not found", null),
        ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden", null),
        CatalogValidationException => (StatusCodes.Status400BadRequest, "Invalid query definition", null),
        QueryExecutionException => (StatusCodes.Status400BadRequest, "Query failed", null),
        ValidationException validationException => (
            StatusCodes.Status400BadRequest,
            "Validation failed",
            validationException.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),
        _ => (StatusCodes.Status500InternalServerError, "Server error", null)
    };
}
