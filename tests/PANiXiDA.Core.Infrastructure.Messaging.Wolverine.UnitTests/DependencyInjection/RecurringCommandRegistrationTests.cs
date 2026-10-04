using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.DependencyInjection;

using Wolverine;
using Wolverine.Runtime.Recurring;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.DependencyInjection;

public sealed class RecurringCommandRegistrationTests
{
    [Theory(DisplayName = "Mediator schedules register recurring services before the container is built")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SchedulesShouldRegisterServicesBeforeContainerBuild(bool useModules, bool includeKafka)
    {
        var configuration = WolverineScheduleConfigurationTests.CreateConfiguration();
        configuration["TestBrokerOption:BootstrapServers"] = "localhost:9092";
        var behaviorConfigured = false;
        Action<WolverineKafkaConfiguration>? configureKafka = includeKafka
            ? kafka => kafka.AddKafkaBroker<TestBrokerOption>()
            : null;
        var builder = ConfigureHost(Host.CreateDefaultBuilder(), configuration, useModules,
            schedules => schedules.AddRecurringCommand<TestRecurringCommandOption, TestCommand>(_ => new TestCommand(Guid.NewGuid())),
            configureKafka, _ => behaviorConfigured = true);

        using var host = builder.Build();

        var options = host.Services.GetRequiredService<WolverineOptions>();
        options.Schedules.FindByName("test-command").ShouldNotBeNull();
        host.Services.GetService<IRecurringScheduleControl>().ShouldNotBeNull();
        behaviorConfigured.ShouldBeTrue();
    }

    [Theory(DisplayName = "Independent mediator hosts rebuild recurring schedules without duplicate registrations")]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentHostsShouldRebuildSchedules(bool useModules)
    {
        var configuration = WolverineScheduleConfigurationTests.CreateConfiguration();
        for (var i = 0; i < 2; i++)
        {
            using var host = ConfigureHost(Host.CreateDefaultBuilder(), configuration, useModules,
                schedules => schedules.AddRecurringCommand<TestRecurringCommandOption, TestCommand>(_ => new TestCommand(Guid.NewGuid())))
                .Build();

            host.Services.GetRequiredService<WolverineOptions>().Schedules.FindByName("test-command").ShouldNotBeNull();
        }
    }

    [Theory(DisplayName = "Mediator schedules reject null required arguments")]
    [InlineData(false, "hostBuilder")]
    [InlineData(false, "configuration")]
    [InlineData(false, "configureSchedules")]
    [InlineData(true, "hostBuilder")]
    [InlineData(true, "configuration")]
    [InlineData(true, "configureSchedules")]
    public void SchedulesShouldRejectNullArguments(bool useModules, string argument)
    {
        var builder = argument == "hostBuilder" ? null! : Host.CreateDefaultBuilder();
        var configuration = argument == "configuration" ? null! : new ConfigurationManager();
        Action<WolverineScheduleConfiguration> configureSchedules = argument == "configureSchedules" ? null! : _ => { };

        var exception = Should.Throw<ArgumentNullException>(() =>
            ConfigureHost(builder, configuration, useModules, configureSchedules));

        exception.ParamName.ShouldBe(argument);
    }

    [Fact(DisplayName = "Mediator schedules reject a null discovery assembly array")]
    public void SchedulesShouldRejectNullDiscoveryAssemblies()
    {
        var builder = Host.CreateDefaultBuilder();

        var exception = Should.Throw<ArgumentNullException>(() => builder.UseWolverineMediator<TestDbContext>(
            "Host=localhost;Database=wolverine", new ConfigurationManager(),
            configureKafka: null, configureRequestBehaviors: null, configureSchedules: _ => { }, discoveryAssemblies: null!));

        exception.ParamName.ShouldBe("discoveryAssemblies");
    }

    private static IHostBuilder ConfigureHost(
        IHostBuilder builder,
        IConfiguration configuration,
        bool useModules,
        Action<WolverineScheduleConfiguration> configureSchedules,
        Action<WolverineKafkaConfiguration>? configureKafka = null,
        Action<WolverineRequestBehaviorConfiguration>? configureBehaviors = null)
    {
        const string connectionString = "Host=localhost;Database=wolverine";
        return useModules
            ? builder.UseWolverineMediator(connectionString, configuration,
                modules => modules.AddModule<TestDbContext>(typeof(RecurringCommandRegistrationTests).Assembly),
                configureKafka, configureBehaviors, configureSchedules)
            : builder.UseWolverineMediator<TestDbContext>(connectionString, configuration,
                configureKafka, configureBehaviors, configureSchedules, typeof(RecurringCommandRegistrationTests).Assembly);
    }
}
