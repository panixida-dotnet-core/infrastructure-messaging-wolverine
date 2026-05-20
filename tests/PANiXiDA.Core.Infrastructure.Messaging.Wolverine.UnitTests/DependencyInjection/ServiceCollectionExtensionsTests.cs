using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.DependencyInjection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact(DisplayName = "AddWolverineMediator registers mediator, event bus and outbox dispatcher")]
    public void AddWolverineMediatorShouldRegisterCoreServices()
    {
        var services = new ServiceCollection();

        services.AddWolverineMediator<TestDbContext>();

        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IMediator) &&
            descriptor.ImplementationType == typeof(WolverineMediator) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IEventBus) &&
            descriptor.ImplementationType == typeof(WolverineEventBus) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IOutboxDispatcher) &&
            descriptor.ImplementationType == typeof(EfCoreOutboxDispatcher<TestDbContext>) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact(DisplayName = "AddWolverineMediator does not replace existing core service registrations")]
    public void AddWolverineMediatorShouldNotReplaceExistingCoreServiceRegistrations()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator, ExistingMediator>();
        services.AddScoped<IEventBus, ExistingEventBus>();

        services.AddWolverineMediator<TestDbContext>();

        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IMediator) &&
            descriptor.ImplementationType == typeof(ExistingMediator));
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IEventBus) &&
            descriptor.ImplementationType == typeof(ExistingEventBus));
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
            typeof(ServiceCollectionExtensionsTests).Assembly);

        var act = async () =>
        {
            using var host = await hostBuilder.StartAsync(TestContext.Current.CancellationToken);
        };

        await act.Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("The Wolverine message store connection string must not be empty. (Parameter 'messageStoreConnectionString')");
    }
}
