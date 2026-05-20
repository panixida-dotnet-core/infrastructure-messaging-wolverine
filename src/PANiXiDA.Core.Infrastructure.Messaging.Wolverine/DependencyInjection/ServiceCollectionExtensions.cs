using JasperFx.CodeGeneration;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

using System.Reflection;

using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

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

    /// <summary>
    /// Configures Wolverine with PostgreSQL message persistence and default mediator policies.
    /// </summary>
    /// <typeparam name="TDbContext">The EF Core DbContext type enrolled in Wolverine message storage.</typeparam>
    /// <param name="hostBuilder">The application host builder.</param>
    /// <param name="messageStoreConnectionString">The PostgreSQL connection string used for Wolverine message storage.</param>
    /// <param name="discoveryAssemblies">The assemblies where Wolverine should discover message handlers.</param>
    /// <returns>The same host builder instance for fluent configuration.</returns>
    public static IHostBuilder UseWolverineMediator<TDbContext>(
        this IHostBuilder hostBuilder,
        string messageStoreConnectionString,
        params Assembly[] discoveryAssemblies)
        where TDbContext : DbContext
    {
        return hostBuilder.UseWolverine(options =>
        {
            ConfigureWolverineMediator<TDbContext>(
                options,
                messageStoreConnectionString,
                configureWolverine: null,
                discoveryAssemblies);
        });
    }

    /// <summary>
    /// Configures Wolverine with PostgreSQL message persistence, default mediator policies, and typed Kafka topology helpers.
    /// </summary>
    /// <typeparam name="TDbContext">The EF Core DbContext type enrolled in Wolverine message storage.</typeparam>
    /// <param name="hostBuilder">The application host builder.</param>
    /// <param name="messageStoreConnectionString">The PostgreSQL connection string used for Wolverine message storage.</param>
    /// <param name="configuration">The application configuration used to resolve typed Kafka options.</param>
    /// <param name="configureKafka">An optional callback for registering typed Kafka brokers, producers, and consumers.</param>
    /// <param name="discoveryAssemblies">The assemblies where Wolverine should discover message handlers.</param>
    /// <returns>The same host builder instance for fluent configuration.</returns>
    public static IHostBuilder UseWolverineMediator<TDbContext>(
        this IHostBuilder hostBuilder,
        string messageStoreConnectionString,
        IConfiguration configuration,
        Action<WolverineKafkaConfiguration>? configureKafka,
        params Assembly[] discoveryAssemblies)
        where TDbContext : DbContext
    {
        return hostBuilder.UseWolverine(options =>
        {
            ConfigureWolverineMediator<TDbContext>(
                options,
                messageStoreConnectionString,
                configuredOptions =>
            {
                var kafkaTopologyBuilder = new WolverineKafkaConfiguration(
                    configuredOptions,
                    configuration);

                configureKafka?.Invoke(kafkaTopologyBuilder);
            },
                discoveryAssemblies);
        });
    }

    private static void ConfigureWolverineMediator<TDbContext>(
        WolverineOptions options,
        string messageStoreConnectionString,
        Action<WolverineOptions>? configureWolverine,
        Assembly[] discoveryAssemblies)
        where TDbContext : DbContext
    {
        if (string.IsNullOrWhiteSpace(messageStoreConnectionString))
        {
            throw new ArgumentException(
                "The Wolverine message store connection string must not be empty.",
                nameof(messageStoreConnectionString));
        }

        options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;

        ConfigureInboxOutbox<TDbContext>(
            options,
            messageStoreConnectionString);

        ConfigureRequestMiddlewares(options);

        configureWolverine?.Invoke(options);

        for (var i = 0; i < discoveryAssemblies.Length; i++)
        {
            var assembly = discoveryAssemblies[i];
            if (assembly is null)
            {
                continue;
            }

            options.Discovery.IncludeAssembly(assembly);
        }
    }

    private static void ConfigureInboxOutbox<TDbContext>(
        WolverineOptions options,
        string messageStoreConnectionString)
        where TDbContext : DbContext
    {
        options
            .PersistMessagesWithPostgresql(messageStoreConnectionString)
            .Enroll<TDbContext>();

        options.UseEntityFrameworkCoreTransactions();

        options.Policies.UseDurableLocalQueues();
        options.Policies.UseDurableInboxOnAllListeners();
        options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    }

    private static void ConfigureRequestMiddlewares(WolverineOptions options)
    {
        var registry = RequestMiddlewareRegistry.Create(builder =>
        {
            builder.AddBefore(typeof(BeginTransactionBehavior<,>));
            builder.AddAfter(typeof(PublishDomainEventsBehavior<,>));
            builder.AddAfter(typeof(SaveChangesBehavior<,>));
            builder.AddAfter(typeof(CommitTransactionBehavior<,>));
            builder.AddAfter(typeof(FlushOutgoingMessagesBehavior<,>));
            builder.AddFinally(typeof(CleanupTransactionBehavior<,>));
        });

        options.Policies.Add(new RequestMiddlewareChainPolicy(registry));
    }
}
