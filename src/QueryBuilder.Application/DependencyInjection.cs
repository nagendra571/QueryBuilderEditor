using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application.Common;

namespace QueryBuilder.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddScoped<ISender, Sender>();
        RegisterHandlers(services, assembly);

        services.AddValidatorsFromAssembly(assembly);

        return services;
    }

    private static void RegisterHandlers(IServiceCollection services, Assembly assembly)
    {
        var openHandlerInterface = typeof(IRequestHandler<,>);

        var registrations =
            from type in assembly.GetTypes()
            where !type.IsAbstract && !type.IsInterface
            from @interface in type.GetInterfaces()
            where @interface.IsGenericType && @interface.GetGenericTypeDefinition() == openHandlerInterface
            select (Service: @interface, Implementation: type);

        foreach (var (service, implementation) in registrations)
        {
            services.AddScoped(service, implementation);
        }
    }
}
