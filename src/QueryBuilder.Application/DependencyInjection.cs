using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.Common;
using QueryBuilder.Application.DataScoping;
using QueryBuilder.Application.Parameters;

namespace QueryBuilder.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddScoped<ISender, Sender>();
        services.AddScoped<IDataScopeGuard, DataScopeGuard>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IParameterBinder, ParameterBinder>();
        services.AddScoped<Hub.ShareTargets>();
        services.AddScoped<IParameterOptionsService, ParameterOptionsService>();
        services.AddScoped<Dashboards.IDashboardGrant, Dashboards.DashboardGrant>();
        services.AddScoped<Dashboards.DashboardGrantResolver>();
        services.AddScoped<Visualizations.VisualizationDataLoader>();
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
