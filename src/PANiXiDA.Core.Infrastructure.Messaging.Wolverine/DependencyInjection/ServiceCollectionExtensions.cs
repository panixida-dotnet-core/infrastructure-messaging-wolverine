using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.DependencyInjection;

/// <summary>
/// Provides dependency injection helpers for PANiXiDA mediator integration backed by Wolverine.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PANiXiDA mediator and event bus adapters backed by Wolverine.
    /// </summary>
    /// <typeparam name="TDbContext">The EF Core DbContext type used by the Wolverine outbox.</typeparam>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same service collection instance for fluent configuration.</returns>
    public static IServiceCollection AddWolverineMediator<TDbContext>(
        this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.TryAddScoped<IMediator, WolverineMediator>();
        services.TryAddScoped<IEventBus, WolverineEventBus>();
        services.TryAddScoped<IOutboxDispatcher, EfCoreOutboxDispatcher<TDbContext>>();

        return services;
    }

    internal static IServiceCollection AddWolverineMediator(
        this IServiceCollection services,
        WolverineModuleRegistry moduleRegistry)
    {
        services.TryAddScoped<IMediator, WolverineMediator>();
        services.TryAddScoped<IEventBus, WolverineEventBus>();

        services.TryAddScoped(serviceProvider =>
            new WolverineModuleExecutionContext(
                serviceProvider,
                moduleRegistry));

        foreach (var registration in moduleRegistry.Registrations)
        {
            services.TryAddKeyedScoped(
                typeof(IOutboxDispatcher),
                registration.DbContextType,
                typeof(EfCoreOutboxDispatcher<>).MakeGenericType(registration.DbContextType));
        }

        services.TryAddScoped<WolverineModuleUnitOfWork>();
        services.TryAddScoped<WolverineModuleOutboxDispatcher>();

        services.AddScoped<IUnitOfWork>(serviceProvider =>
            serviceProvider.GetRequiredService<WolverineModuleUnitOfWork>());
        services.AddScoped<IOutboxDispatcher>(serviceProvider =>
            serviceProvider.GetRequiredService<WolverineModuleOutboxDispatcher>());

        return services;
    }
}
