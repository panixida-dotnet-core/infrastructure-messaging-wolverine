using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.DependencyInjection;

using Wolverine;

using HostBuilderExtensions = PANiXiDA.Core.Infrastructure.Messaging.Wolverine.DependencyInjection.HostBuilderExtensions;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.DependencyInjection;

public sealed class HostBuilderExtensionsTests
{
    [Theory(DisplayName = "Mediator registrations enable dead letter expiration after seven days")]
    [InlineData(false)]
    [InlineData(true)]
    public void MediatorRegistrationsShouldEnableDeadLetterExpirationAfterSevenDays(bool useModuleRouting)
    {
        var hostBuilder = CreateHostBuilder(useModuleRouting);

        using var host = hostBuilder.Build();
        var options = host.Services.GetRequiredService<WolverineOptions>();

        options.Durability.DeadLetterQueueExpirationEnabled.ShouldBeTrue();
        options.Durability.DeadLetterQueueExpiration.ShouldBe(TimeSpan.FromDays(7));
    }

    [Theory(DisplayName = "Applications can override the default dead letter expiration settings")]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplicationsShouldBeAbleToOverrideDeadLetterExpirationSettings(bool useModuleRouting)
    {
        var hostBuilder = CreateHostBuilder(useModuleRouting);
        hostBuilder.ConfigureServices(services => services.ConfigureWolverine(options =>
        {
            options.Durability.DeadLetterQueueExpirationEnabled = false;
            options.Durability.DeadLetterQueueExpiration = TimeSpan.FromDays(14);
        }));

        using var host = hostBuilder.Build();
        var options = host.Services.GetRequiredService<WolverineOptions>();

        options.Durability.DeadLetterQueueExpirationEnabled.ShouldBeFalse();
        options.Durability.DeadLetterQueueExpiration.ShouldBe(TimeSpan.FromDays(14));
    }

    [Fact(DisplayName = "ResolveApplicationAssembly uses the entry assembly or executing assembly fallback")]
    public void ResolveApplicationAssemblyShouldUseEntryAssemblyOrExecutingAssemblyFallback()
    {
        var entryAssembly = typeof(HostBuilderExtensionsTests).Assembly;

        var resolvedEntryAssembly = HostBuilderExtensions.ResolveApplicationAssembly(entryAssembly);
        var resolvedFallbackAssembly = HostBuilderExtensions.ResolveApplicationAssembly(entryAssembly: null);

        resolvedEntryAssembly.ShouldBe(entryAssembly);
        resolvedFallbackAssembly.ShouldBe(typeof(HostBuilderExtensions).Assembly);
    }

    [Fact(DisplayName = "Kafka configuration callbacks are optional for modular and generic registration")]
    public void KafkaConfigurationCallbacksShouldBeOptionalForModularAndGenericRegistration()
    {
        const string connectionString = "Host=localhost;Database=wolverine";
        var configuration = new ConfigurationManager();
        var modularKafkaConfigured = false;
        var genericKafkaConfigured = false;

        static void configureModules(WolverineModuleConfiguration modules)
        {
            modules.AddModule<TestDbContext>(typeof(HostBuilderExtensionsTests).Assembly);
        }

        var modularWithoutKafka = Host.CreateDefaultBuilder();
        var modularWithKafka = Host.CreateDefaultBuilder();
        var genericWithoutKafka = Host.CreateDefaultBuilder();
        var genericWithKafka = Host.CreateDefaultBuilder();

        modularWithoutKafka.UseWolverineMediator(
            connectionString,
            configuration,
            configureModules,
            configureKafka: null);
        modularWithKafka.UseWolverineMediator(
            connectionString,
            configuration,
            configureModules,
            configureKafka: _ => modularKafkaConfigured = true);
        genericWithoutKafka.UseWolverineMediator<TestDbContext>(
            connectionString,
            configuration,
            configureKafka: null,
            typeof(HostBuilderExtensionsTests).Assembly);
        genericWithKafka.UseWolverineMediator<TestDbContext>(
            connectionString,
            configuration,
            configureKafka: _ => genericKafkaConfigured = true,
            typeof(HostBuilderExtensionsTests).Assembly);

        using var modularWithoutKafkaHost = modularWithoutKafka.Build();
        using var modularWithKafkaHost = modularWithKafka.Build();
        using var genericWithoutKafkaHost = genericWithoutKafka.Build();
        using var genericWithKafkaHost = genericWithKafka.Build();

        modularKafkaConfigured.ShouldBeTrue();
        genericKafkaConfigured.ShouldBeTrue();
    }

    [Fact(DisplayName = "UseWolverineMediator behavior overload validates message store connection string")]
    public async Task UseWolverineMediatorBehaviorOverloadShouldValidateMessageStoreConnectionString()
    {
        var hostBuilder = Host
            .CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<TestDbContext>();
                services.AddWolverineMediator<TestDbContext>();
            });

        hostBuilder.UseWolverineMediator<TestDbContext>(
            " ",
            behaviors =>
            {
                behaviors.Before.InsertAfter(
                    typeof(ClosedCommandBeforeBehavior),
                    typeof(BeginTransactionBehavior<,>));
            },
            typeof(HostBuilderExtensionsTests).Assembly);

        async Task act()
        {
            using var host = await hostBuilder.StartAsync(TestContext.Current.CancellationToken);
        }

        var exception = await Should.ThrowAsync<ArgumentException>(act);

        exception.Message.ShouldBe(
            "The Wolverine message store connection string must not be empty. (Parameter 'messageStoreConnectionString')");
    }

    [Fact(DisplayName = "Modular UseWolverineMediator validates message store connection string")]
    public async Task ModularUseWolverineMediatorShouldValidateMessageStoreConnectionString()
    {
        var hostBuilder = Host.CreateDefaultBuilder();

        hostBuilder.UseWolverineMediator(
            " ",
            modules => modules.AddModule<TestDbContext>(
                typeof(HostBuilderExtensionsTests).Assembly));

        async Task act()
        {
            using var host = await hostBuilder.StartAsync(
                TestContext.Current.CancellationToken);
        }

        var exception = await Should.ThrowAsync<ArgumentException>(act);

        exception.Message.ShouldBe(
            "The Wolverine message store connection string must not be empty. (Parameter 'messageStoreConnectionString')");
    }

    private static IHostBuilder CreateHostBuilder(bool useModuleRouting)
    {
        const string connectionString = "Host=localhost;Database=wolverine";
        var hostBuilder = Host.CreateDefaultBuilder();

        return useModuleRouting
            ? hostBuilder.UseWolverineMediator(
                connectionString,
                modules => modules.AddModule<TestDbContext>(typeof(HostBuilderExtensionsTests).Assembly))
            : hostBuilder.UseWolverineMediator<TestDbContext>(
                connectionString,
                typeof(HostBuilderExtensionsTests).Assembly);
    }
}
