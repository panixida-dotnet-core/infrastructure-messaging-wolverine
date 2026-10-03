using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Application.Messaging.Scheduling;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.DependencyInjection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.DependencyInjection;

public sealed class SchedulerRegistrationTests
{
    [Theory(DisplayName = "Scheduler follows nested module scopes and restores the parent outbox")]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task SchedulerShouldFollowActiveModule(bool isCommand, bool absoluteTime)
    {
        var services = new ServiceCollection();
        var registry = new WolverineModuleConfiguration()
            .AddModule<TestDbContext>(typeof(TestCommand).Assembly)
            .AddModule<SecondTestDbContext>(typeof(DbContext).Assembly)
            .Build();
        services.AddSingleton(DbContextOutboxProxy<TestDbContext>.Create(out var firstOutbox));
        services.AddSingleton(DbContextOutboxProxy<SecondTestDbContext>.Create(out var secondOutbox));
        services.AddWolverineMediator(registry);
        services.AddWolverineMediator(registry);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        await using var otherScope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WolverineModuleExecutionContext>();
        var scheduler = scope.ServiceProvider.GetRequiredService<IScheduler>();
        scheduler.ShouldBeOfType<WolverineScheduler>();
        scope.ServiceProvider.GetRequiredService<IScheduler>().ShouldBeSameAs(scheduler);
        otherScope.ServiceProvider.GetRequiredService<IScheduler>().ShouldNotBeSameAs(scheduler);
        var command = new TestCommand(Guid.NewGuid());
        var @event = new TestDomainEvent(Guid.NewGuid());
        var token = TestContext.Current.CancellationToken;
        var deliverAt = DateTimeOffset.UtcNow.AddHours(1);
        Task schedule() => (isCommand, absoluteTime) switch
        {
            (true, true) => scheduler.ScheduleAtAsync(command, deliverAt, token),
            (true, false) => scheduler.ScheduleAsync(command, TimeSpan.FromMinutes(15), token),
            (false, true) => scheduler.ScheduleAtAsync(@event, deliverAt, token),
            (false, false) => scheduler.ScheduleAsync(@event, TimeSpan.FromMinutes(15), token)
        };

        context.Enter(typeof(TestCommand));
        await schedule();
        context.Enter(typeof(DbContext));
        await schedule();
        context.Exit(typeof(DbContext));
        await schedule();
        context.Exit(typeof(TestCommand));

        firstOutbox.SendCallCount.ShouldBe(isCommand ? 2 : 0);
        secondOutbox.SendCallCount.ShouldBe(isCommand ? 1 : 0);
        firstOutbox.PublishCallCount.ShouldBe(isCommand ? 0 : 2);
        secondOutbox.PublishCallCount.ShouldBe(isCommand ? 0 : 1);
        foreach (var dbContextType in new[] { typeof(TestDbContext), typeof(SecondTestDbContext) })
        {
            scope.ServiceProvider.GetKeyedServices<IScheduler>(dbContextType).ShouldBeEmpty();
        }
    }

    [Fact(DisplayName = "Single context registration resolves and preserves scoped schedulers")]
    public async Task RegistrationShouldPreserveSchedulers()
    {
        var services = new ServiceCollection();
        services.AddSingleton(DbContextOutboxProxy<TestDbContext>.Create(out var proxy));
        services.AddWolverineMediator<TestDbContext>();
        services.AddWolverineMediator<TestDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var scheduler = scope.ServiceProvider.GetServices<IScheduler>().ShouldHaveSingleItem();

        await scheduler.ScheduleAsync(new TestCommand(Guid.NewGuid()), TimeSpan.Zero, TestContext.Current.CancellationToken);

        proxy.SendCallCount.ShouldBe(1);
        var customServices = new ServiceCollection();
        customServices.AddSingleton(scheduler);
        customServices.AddWolverineMediator<TestDbContext>();
        await using var customProvider = customServices.BuildServiceProvider();
        customProvider.GetServices<IScheduler>().ShouldHaveSingleItem().ShouldBeSameAs(scheduler);
    }

    [Theory(DisplayName = "Scheduler uses the existing outbox dispatcher in single and modular setups")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SchedulerShouldUseExistingOutboxDispatcher(bool useModules)
    {
        var services = new ServiceCollection();
        var dispatcher = new TestOutboxDispatcher();
        if (useModules)
        {
            services.AddKeyedSingleton<IOutboxDispatcher>(typeof(TestDbContext), dispatcher);
            services.AddWolverineMediator(new WolverineModuleConfiguration()
                .AddModule<TestDbContext>(typeof(TestCommand).Assembly)
                .Build());
        }
        else
        {
            services.AddSingleton<IOutboxDispatcher>(dispatcher);
            services.AddWolverineMediator<TestDbContext>();
        }

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        if (useModules)
        {
            scope.ServiceProvider.GetRequiredService<WolverineModuleExecutionContext>().Enter(typeof(TestCommand));
        }

        var scheduler = scope.ServiceProvider.GetRequiredService<IScheduler>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        var command = new TestCommand(Guid.NewGuid());
        var scheduledEvent = new TestDomainEvent(Guid.NewGuid());
        var token = TestContext.Current.CancellationToken;

        await eventBus.PublishAsync(new TestDomainEvent(Guid.NewGuid()), token);
        await scheduler.ScheduleAsync(command, TimeSpan.FromMinutes(15), token);
        await scheduler.ScheduleAsync(scheduledEvent, TimeSpan.FromMinutes(15), token);

        dispatcher.SendCallCount.ShouldBe(1);
        dispatcher.LastSentMessage.ShouldBeSameAs(command);
        dispatcher.PublishCallCount.ShouldBe(2);
        dispatcher.LastPublishedEvent.ShouldBeSameAs(scheduledEvent);
        dispatcher.LastDispatchCancellationToken.ShouldBe(token);
        dispatcher.PersistCallCount.ShouldBe(0);
        dispatcher.FlushCallCount.ShouldBe(0);
    }
}
