using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;

using FluentValidation;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

using System.Reflection;

using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.DependencyInjection;

/// <summary>
/// Provides host builder helpers for PANiXiDA mediator integration backed by Wolverine.
/// </summary>
public static class HostBuilderExtensions
{
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
        return RegisterFluentValidationValidators(
            hostBuilder,
            discoveryAssemblies)
            .UseWolverine(options =>
        {
            ConfigureWolverineMediator<TDbContext>(
                options,
                messageStoreConnectionString,
                configureWolverine: null,
                configureRequestBehaviors: null,
                discoveryAssemblies);
        });
    }

    /// <summary>
    /// Configures one Wolverine runtime and PostgreSQL message store for multiple EF Core modules.
    /// </summary>
    /// <param name="hostBuilder">The application host builder.</param>
    /// <param name="messageStoreConnectionString">The PostgreSQL connection string used for Wolverine message storage.</param>
    /// <param name="messageStoreSchemaName">The shared PostgreSQL schema used for Wolverine message storage.</param>
    /// <param name="configureModules">The callback that registers module DbContexts and assemblies.</param>
    /// <param name="configureRequestBehaviors">An optional callback for configuring request behaviors.</param>
    /// <returns>The same host builder instance for fluent configuration.</returns>
    public static IHostBuilder UseWolverineMediator(
        this IHostBuilder hostBuilder,
        string messageStoreConnectionString,
        string messageStoreSchemaName,
        Action<WolverineModuleConfiguration> configureModules,
        Action<WolverineRequestBehaviorConfiguration>? configureRequestBehaviors = null)
    {
        return UseModularWolverineMediator(
            hostBuilder,
            messageStoreConnectionString,
            messageStoreSchemaName,
            configureModules,
            configureWolverine: null,
            configureRequestBehaviors);
    }

    /// <summary>
    /// Configures one Wolverine runtime, PostgreSQL message store, and Kafka topology for multiple EF Core modules.
    /// </summary>
    /// <param name="hostBuilder">The application host builder.</param>
    /// <param name="messageStoreConnectionString">The PostgreSQL connection string used for Wolverine message storage.</param>
    /// <param name="messageStoreSchemaName">The shared PostgreSQL schema used for Wolverine message storage.</param>
    /// <param name="configuration">The application configuration used to resolve typed Kafka options.</param>
    /// <param name="configureModules">The callback that registers module DbContexts and assemblies.</param>
    /// <param name="configureKafka">An optional callback for registering typed Kafka brokers, producers, and consumers.</param>
    /// <param name="configureRequestBehaviors">An optional callback for configuring request behaviors.</param>
    /// <returns>The same host builder instance for fluent configuration.</returns>
    public static IHostBuilder UseWolverineMediator(
        this IHostBuilder hostBuilder,
        string messageStoreConnectionString,
        string messageStoreSchemaName,
        IConfiguration configuration,
        Action<WolverineModuleConfiguration> configureModules,
        Action<WolverineKafkaConfiguration>? configureKafka,
        Action<WolverineRequestBehaviorConfiguration>? configureRequestBehaviors = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return UseModularWolverineMediator(
            hostBuilder,
            messageStoreConnectionString,
            messageStoreSchemaName,
            configureModules,
            options =>
            {
                var kafkaTopologyBuilder = new WolverineKafkaConfiguration(
                    options,
                    configuration);

                configureKafka?.Invoke(kafkaTopologyBuilder);
            },
            configureRequestBehaviors);
    }

    /// <summary>
    /// Configures Wolverine with PostgreSQL message persistence and configurable mediator request behaviors.
    /// </summary>
    /// <typeparam name="TDbContext">The EF Core DbContext type enrolled in Wolverine message storage.</typeparam>
    /// <param name="hostBuilder">The application host builder.</param>
    /// <param name="messageStoreConnectionString">The PostgreSQL connection string used for Wolverine message storage.</param>
    /// <param name="configureRequestBehaviors">An optional callback for configuring request behaviors.</param>
    /// <param name="discoveryAssemblies">The assemblies where Wolverine should discover message handlers.</param>
    /// <returns>The same host builder instance for fluent configuration.</returns>
    public static IHostBuilder UseWolverineMediator<TDbContext>(
        this IHostBuilder hostBuilder,
        string messageStoreConnectionString,
        Action<WolverineRequestBehaviorConfiguration>? configureRequestBehaviors,
        params Assembly[] discoveryAssemblies)
        where TDbContext : DbContext
    {
        return RegisterFluentValidationValidators(
            hostBuilder,
            discoveryAssemblies)
            .UseWolverine(options =>
        {
            ConfigureWolverineMediator<TDbContext>(
                options,
                messageStoreConnectionString,
                configureWolverine: null,
                configureRequestBehaviors,
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
        return hostBuilder.UseWolverineMediator<TDbContext>(
            messageStoreConnectionString,
            configuration,
            configureKafka,
            configureRequestBehaviors: null,
            discoveryAssemblies);
    }

    /// <summary>
    /// Configures Wolverine with PostgreSQL message persistence, configurable mediator request behaviors, and typed Kafka topology helpers.
    /// </summary>
    /// <typeparam name="TDbContext">The EF Core DbContext type enrolled in Wolverine message storage.</typeparam>
    /// <param name="hostBuilder">The application host builder.</param>
    /// <param name="messageStoreConnectionString">The PostgreSQL connection string used for Wolverine message storage.</param>
    /// <param name="configuration">The application configuration used to resolve typed Kafka options.</param>
    /// <param name="configureKafka">An optional callback for registering typed Kafka brokers, producers, and consumers.</param>
    /// <param name="configureRequestBehaviors">An optional callback for configuring request behaviors.</param>
    /// <param name="discoveryAssemblies">The assemblies where Wolverine should discover message handlers.</param>
    /// <returns>The same host builder instance for fluent configuration.</returns>
    public static IHostBuilder UseWolverineMediator<TDbContext>(
        this IHostBuilder hostBuilder,
        string messageStoreConnectionString,
        IConfiguration configuration,
        Action<WolverineKafkaConfiguration>? configureKafka,
        Action<WolverineRequestBehaviorConfiguration>? configureRequestBehaviors,
        params Assembly[] discoveryAssemblies)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return RegisterFluentValidationValidators(
            hostBuilder,
            discoveryAssemblies)
            .UseWolverine(options =>
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
                configureRequestBehaviors,
                discoveryAssemblies);
        });
    }

    private static IHostBuilder UseModularWolverineMediator(
        IHostBuilder hostBuilder,
        string messageStoreConnectionString,
        string messageStoreSchemaName,
        Action<WolverineModuleConfiguration> configureModules,
        Action<WolverineOptions>? configureWolverine,
        Action<WolverineRequestBehaviorConfiguration>? configureRequestBehaviors)
    {
        ArgumentNullException.ThrowIfNull(configureModules);

        var moduleConfiguration = new WolverineModuleConfiguration();
        configureModules(moduleConfiguration);
        var moduleRegistry = moduleConfiguration.Build();
        var discoveryAssemblies = moduleRegistry.DiscoveryAssemblies.ToArray();

        return RegisterFluentValidationValidators(
            hostBuilder,
            discoveryAssemblies)
            .ConfigureServices(services =>
                services.AddWolverineMediator(moduleRegistry))
            .UseWolverine(options =>
            {
                ConfigureModularWolverineMediator(
                    options,
                    messageStoreConnectionString,
                    messageStoreSchemaName,
                    moduleRegistry,
                    configureWolverine,
                    configureRequestBehaviors);
            });
    }

    private static IHostBuilder RegisterFluentValidationValidators(
        IHostBuilder hostBuilder,
        Assembly[] discoveryAssemblies)
    {
        return hostBuilder.ConfigureServices(services =>
        {
            services.AddValidatorsFromAssemblies(
                discoveryAssemblies.OfType<Assembly>(),
                includeInternalTypes: true);
        });
    }

    private static void ConfigureWolverineMediator<TDbContext>(
        WolverineOptions options,
        string messageStoreConnectionString,
        Action<WolverineOptions>? configureWolverine,
        Action<WolverineRequestBehaviorConfiguration>? configureRequestBehaviors,
        Assembly[] discoveryAssemblies)
        where TDbContext : DbContext
    {
        if (string.IsNullOrWhiteSpace(messageStoreConnectionString))
        {
            throw new ArgumentException(
                "The Wolverine message store connection string must not be empty.",
                nameof(messageStoreConnectionString));
        }

        options.ApplicationAssembly = ResolveApplicationAssembly();
        options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;

        ConfigureInboxOutbox<TDbContext>(
            options,
            messageStoreConnectionString);

        ConfigureRequestMiddlewares(
            options,
            configureRequestBehaviors);

        configureWolverine?.Invoke(options);

        for (var i = 0; i < discoveryAssemblies.Length; i++)
        {
            var assembly = discoveryAssemblies[i];
            if (assembly is not null)
            {
                options.Discovery.IncludeAssembly(assembly);
            }
        }
    }

    private static void ConfigureModularWolverineMediator(
        WolverineOptions options,
        string messageStoreConnectionString,
        string messageStoreSchemaName,
        WolverineModuleRegistry moduleRegistry,
        Action<WolverineOptions>? configureWolverine,
        Action<WolverineRequestBehaviorConfiguration>? configureRequestBehaviors)
    {
        ValidateMessageStoreConnectionString(messageStoreConnectionString);

        if (string.IsNullOrWhiteSpace(messageStoreSchemaName))
        {
            throw new ArgumentException(
                "The Wolverine message store schema name must not be empty.",
                nameof(messageStoreSchemaName));
        }

        options.ApplicationAssembly = ResolveApplicationAssembly();
        options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
        options.ServiceLocationPolicy = ServiceLocationPolicy.AlwaysAllowed;
        options.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;
        options.Durability.MessageIdentity = MessageIdentity.IdAndDestination;

        ConfigureInboxOutbox(
            options,
            messageStoreConnectionString,
            messageStoreSchemaName,
            moduleRegistry);

        ConfigureRequestMiddlewares(
            options,
            configureRequestBehaviors,
            useModuleRouting: true);

        options.Policies.Add(new WolverineModuleTransactionPolicy());

        configureWolverine?.Invoke(options);

        foreach (var assembly in moduleRegistry.DiscoveryAssemblies)
        {
            options.Discovery.IncludeAssembly(assembly);
        }
    }

    private static Assembly ResolveApplicationAssembly()
    {
        return Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
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

    private static void ConfigureInboxOutbox(
        WolverineOptions options,
        string messageStoreConnectionString,
        string messageStoreSchemaName,
        WolverineModuleRegistry moduleRegistry)
    {
        var storage = options.PersistMessagesWithPostgresql(
            messageStoreConnectionString,
            messageStoreSchemaName);

        foreach (var dbContextType in moduleRegistry.Registrations
                     .Select(registration => registration.DbContextType))
        {
            storage.Enroll(dbContextType);
        }

        options.UseEntityFrameworkCoreTransactions();

        options.Policies.UseDurableLocalQueues();
        options.Policies.UseDurableInboxOnAllListeners();
        options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    }

    private static void ConfigureRequestMiddlewares(
        WolverineOptions options,
        Action<WolverineRequestBehaviorConfiguration>? configureRequestBehaviors,
        bool useModuleRouting = false)
    {
        var configuration = useModuleRouting
            ? WolverineRequestBehaviorConfiguration.CreateModularDefault()
            : WolverineRequestBehaviorConfiguration.CreateDefault();
        configureRequestBehaviors?.Invoke(configuration);

        options.Policies.Add(new RequestMiddlewareChainPolicy(configuration.Build()));
    }

    private static void ValidateMessageStoreConnectionString(
        string messageStoreConnectionString)
    {
        if (string.IsNullOrWhiteSpace(messageStoreConnectionString))
        {
            throw new ArgumentException(
                "The Wolverine message store connection string must not be empty.",
                nameof(messageStoreConnectionString));
        }
    }

}
