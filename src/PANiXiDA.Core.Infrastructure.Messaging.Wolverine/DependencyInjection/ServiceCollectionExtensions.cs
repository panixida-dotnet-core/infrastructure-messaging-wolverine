using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PANiXiDA.Core.Application.Messaging.Scheduling;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

using Wolverine.EntityFrameworkCore;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.DependencyInjection;

/// <summary>
/// Provides dependency injection helpers for PANiXiDA mediator integration backed by Wolverine.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PANiXiDA mediator, event bus, and scheduler adapters backed by Wolverine.
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
        services.TryAddScoped<IScheduler>(serviceProvider =>
            new WolverineScheduler(serviceProvider.GetRequiredService<IDbContextOutbox<TDbContext>>()));

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
            services.TryAdd(registration.OutboxDispatcher);
            services.TryAdd(registration.Scheduler);
        }

        services.TryAddScoped<WolverineModuleUnitOfWork>();
        services.TryAddScoped<WolverineModuleOutboxDispatcher>();

        services.AddScoped<IUnitOfWork>(serviceProvider =>
            serviceProvider.GetRequiredService<WolverineModuleUnitOfWork>());
        services.AddScoped<IOutboxDispatcher>(serviceProvider =>
            serviceProvider.GetRequiredService<WolverineModuleOutboxDispatcher>());
        services.AddScoped<IScheduler, WolverineModuleScheduler>();

        return services;
    }
}
