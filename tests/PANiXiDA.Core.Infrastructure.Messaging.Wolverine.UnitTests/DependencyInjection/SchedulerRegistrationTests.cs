using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Application.Messaging.Scheduling;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;
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
            var keyed = scope.ServiceProvider.GetKeyedServices<IScheduler>(dbContextType).ShouldHaveSingleItem();
            scope.ServiceProvider.GetRequiredKeyedService<IScheduler>(dbContextType).ShouldBeSameAs(keyed);
            otherScope.ServiceProvider.GetRequiredKeyedService<IScheduler>(dbContextType).ShouldNotBeSameAs(keyed);
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

    [Fact(DisplayName = "Module context reports missing scheduler registrations")]
    public void ModuleContextShouldReportMissingScheduler()
    {
        var registry = new WolverineModuleConfiguration()
            .AddModule<TestDbContext>(typeof(TestCommand).Assembly)
            .Build();
        using var provider = new ServiceCollection().BuildServiceProvider();
        var context = new WolverineModuleExecutionContext(provider, registry);

        context.TryGetScheduler(out _).ShouldBeFalse();
        context.Enter(typeof(TestCommand));

        Should.Throw<InvalidOperationException>(() => context.TryGetScheduler(out _))
            .Message.ShouldContain("No Wolverine scheduler is registered");
    }
}
