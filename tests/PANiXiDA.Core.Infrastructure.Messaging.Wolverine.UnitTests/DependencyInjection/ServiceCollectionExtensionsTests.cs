using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;
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

    [Fact(DisplayName = "Modular registration routes unit of work and outbox through the active module")]
    public async Task ModularRegistrationShouldRoutePersistenceServicesThroughActiveModule()
    {
        var services = new ServiceCollection();
        var moduleConfiguration = new WolverineModuleConfiguration()
            .AddModule<TestDbContext>(typeof(TestCommand).Assembly);
        var moduleUnitOfWork = new TestUnitOfWork();
        var moduleOutboxDispatcher = new TestOutboxDispatcher();

        services.AddWolverineMediator(moduleConfiguration.Build());
        services.AddKeyedScoped<IUnitOfWork>(
            typeof(TestDbContext),
            (_, _) => moduleUnitOfWork);
        services.AddKeyedScoped<IOutboxDispatcher>(
            typeof(TestDbContext),
            (_, _) => moduleOutboxDispatcher);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var moduleContext = scope.ServiceProvider
            .GetRequiredService<WolverineModuleExecutionContext>();
        moduleContext.Enter(typeof(TestCommand));

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var outboxDispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        await unitOfWork.BeginTransactionAsync(TestContext.Current.CancellationToken);
        unitOfWork.HasActiveTransaction.ShouldBeTrue();
        await unitOfWork.CommitTransactionAsync(TestContext.Current.CancellationToken);
        await unitOfWork.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await unitOfWork.RollbackTransactionAsync(TestContext.Current.CancellationToken);
        await unitOfWork.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await unitOfWork.DisposeTransactionAsync();
        await unitOfWork.ExecuteInTransactionAsync(
            _ => Task.CompletedTask,
            TestContext.Current.CancellationToken);
        await eventBus.PublishAsync(
            new TestDomainEvent(Guid.NewGuid()),
            TestContext.Current.CancellationToken);
        await outboxDispatcher.FlushAsync(TestContext.Current.CancellationToken);

        moduleUnitOfWork.BeginTransactionCallCount.ShouldBe(4);
        moduleUnitOfWork.CommitTransactionCallCount.ShouldBe(2);
        moduleUnitOfWork.RollbackTransactionCallCount.ShouldBe(1);
        moduleUnitOfWork.DisposeTransactionCallCount.ShouldBe(1);
        moduleOutboxDispatcher.PublishCallCount.ShouldBe(1);
        moduleOutboxDispatcher.FlushCallCount.ShouldBe(1);

        moduleContext.Exit(typeof(TestCommand));
    }

    [Fact(DisplayName = "Modular event bus uses the active Wolverine message context outside mediator requests")]
    public async Task ModularEventBusShouldUseActiveWolverineMessageContextOutsideMediatorRequests()
    {
        var services = new ServiceCollection();
        var moduleConfiguration = new WolverineModuleConfiguration()
            .AddModule<TestDbContext>(typeof(TestCommand).Assembly);
        var messageContext = MessageContextProxy.Create(out var proxy);

        services.AddWolverineMediator(moduleConfiguration.Build());
        services.AddScoped(_ => messageContext);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        var outboxDispatcher = scope.ServiceProvider
            .GetRequiredService<IOutboxDispatcher>();
        var domainEvent = new TestDomainEvent(Guid.NewGuid());

        await eventBus.PublishAsync(
            domainEvent,
            TestContext.Current.CancellationToken);
        await outboxDispatcher.FlushAsync(
            TestContext.Current.CancellationToken);

        proxy.PublishCallCount.ShouldBe(1);
        proxy.LastPublishedMessage.ShouldBeSameAs(domainEvent);
    }
}
