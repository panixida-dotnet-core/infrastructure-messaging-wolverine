using Microsoft.Extensions.DependencyInjection;

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

        services.Count(descriptor =>
            descriptor.ServiceType == typeof(IMediator) &&
            descriptor.ImplementationType == typeof(WolverineMediator) &&
            descriptor.Lifetime == ServiceLifetime.Scoped)
            .ShouldBe(1);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(IEventBus) &&
            descriptor.ImplementationType == typeof(WolverineEventBus) &&
            descriptor.Lifetime == ServiceLifetime.Scoped)
            .ShouldBe(1);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(IOutboxDispatcher) &&
            descriptor.ImplementationType == typeof(EfCoreOutboxDispatcher<TestDbContext>) &&
            descriptor.Lifetime == ServiceLifetime.Scoped)
            .ShouldBe(1);
    }

    [Fact(DisplayName = "AddWolverineMediator does not replace existing core service registrations")]
    public void AddWolverineMediatorShouldNotReplaceExistingCoreServiceRegistrations()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator, ExistingMediator>();
        services.AddScoped<IEventBus, ExistingEventBus>();

        services.AddWolverineMediator<TestDbContext>();

        services.Count(descriptor =>
            descriptor.ServiceType == typeof(IMediator) &&
            descriptor.ImplementationType == typeof(ExistingMediator))
            .ShouldBe(1);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(IEventBus) &&
            descriptor.ImplementationType == typeof(ExistingEventBus))
            .ShouldBe(1);
    }
}
