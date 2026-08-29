using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.DependencyInjection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.DependencyInjection;

public sealed class HostBuilderExtensionsTests
{
    [Fact(DisplayName = "Kafka configuration callbacks are optional for modular and generic registration")]
    public void KafkaConfigurationCallbacksShouldBeOptionalForModularAndGenericRegistration()
    {
        const string connectionString = "Host=localhost;Database=wolverine";
        var configuration = new ConfigurationManager();

        static void configureModules(WolverineModuleConfiguration modules)
        {
            modules.AddModule<TestDbContext>(typeof(HostBuilderExtensionsTests).Assembly);
        }

        var modularWithoutKafka = Host.CreateDefaultBuilder();
        var modularWithKafka = Host.CreateDefaultBuilder();
        var genericWithoutKafka = Host.CreateDefaultBuilder();
        var genericWithKafka = Host.CreateDefaultBuilder();

        var modularWithoutKafkaResult = modularWithoutKafka.UseWolverineMediator(
            connectionString,
            configuration,
            configureModules,
            configureKafka: null);
        var modularWithKafkaResult = modularWithKafka.UseWolverineMediator(
            connectionString,
            configuration,
            configureModules,
            configureKafka: _ => { });
        var genericWithoutKafkaResult = genericWithoutKafka.UseWolverineMediator<TestDbContext>(
            connectionString,
            configuration,
            configureKafka: null,
            typeof(HostBuilderExtensionsTests).Assembly);
        var genericWithKafkaResult = genericWithKafka.UseWolverineMediator<TestDbContext>(
            connectionString,
            configuration,
            configureKafka: _ => { },
            typeof(HostBuilderExtensionsTests).Assembly);

        modularWithoutKafkaResult.ShouldBeSameAs(modularWithoutKafka);
        modularWithKafkaResult.ShouldBeSameAs(modularWithKafka);
        genericWithoutKafkaResult.ShouldBeSameAs(genericWithoutKafka);
        genericWithKafkaResult.ShouldBeSameAs(genericWithKafka);
    }

    [Fact(DisplayName = "ResolveApplicationAssembly uses provided entry assembly and executing assembly fallback")]
    public void ResolveApplicationAssemblyShouldUseProvidedEntryAssemblyAndExecutingAssemblyFallback()
    {
        var entryAssembly = typeof(HostBuilderExtensionsTests).Assembly;

        var resolvedEntryAssembly = HostBuilderExtensions.ResolveApplicationAssembly(
            () => entryAssembly);
        var resolvedFallbackAssembly = HostBuilderExtensions.ResolveApplicationAssembly(
            () => null);

        resolvedEntryAssembly.ShouldBe(entryAssembly);
        resolvedFallbackAssembly.ShouldBe(typeof(HostBuilderExtensions).Assembly);
    }

    [Fact(DisplayName = "ValidateMessageStoreConnectionString accepts a non-empty connection string")]
    public void ValidateMessageStoreConnectionStringShouldAcceptNonEmptyConnectionString()
    {
        static void act()
        {
            HostBuilderExtensions.ValidateMessageStoreConnectionString(
                "Host=localhost;Database=wolverine");
        }

        Should.NotThrow(act);
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
}
