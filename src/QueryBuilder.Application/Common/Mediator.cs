using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace QueryBuilder.Application.Common;

/// <summary>
/// A minimal in-process request dispatcher (Command/Query + Handler shape), written in-house
/// to avoid taking a dependency on MediatR's commercially-licensed releases. Deliberately small:
/// no pipeline/notification infrastructure beyond the FluentValidation pass every request needs.
/// </summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}

public interface IRequest<out TResponse>;

public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}

public sealed class Sender(IServiceProvider serviceProvider) : ISender
{
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        var requestType = request.GetType();

        var validatorType = typeof(IValidator<>).MakeGenericType(requestType);
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (dynamic validator in serviceProvider.GetServices(validatorType))
        {
            FluentValidation.Results.ValidationResult result = await validator.ValidateAsync((dynamic)request, cancellationToken);
            failures.AddRange(result.Errors);
        }
        if (failures.Count != 0)
        {
            throw new ValidationException(failures);
        }

        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
        dynamic handler = serviceProvider.GetRequiredService(handlerType);
        return await handler.Handle((dynamic)request, cancellationToken);
    }
}
