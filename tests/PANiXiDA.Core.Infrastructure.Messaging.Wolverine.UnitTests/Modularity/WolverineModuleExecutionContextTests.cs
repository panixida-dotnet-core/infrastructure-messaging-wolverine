using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.DependencyInjection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Modularity;

public sealed class WolverineModuleExecutionContextTests
{
    [Fact(DisplayName = "Execution context resolves nested module services in stack order")]
    public void ExecutionContextShouldResolveNestedModuleServicesInStackOrder()
    {
        var firstUnitOfWork = new TestUnitOfWork();
        var secondUnitOfWork = new TestUnitOfWork();
        var firstOutbox = new TestOutboxDispatcher();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IUnitOfWork>(
            typeof(TestDbContext),
            firstUnitOfWork);
        services.AddKeyedSingleton<IUnitOfWork>(
            typeof(SecondTestDbContext),
            secondUnitOfWork);
        services.AddKeyedSingleton<IOutboxDispatcher>(
            typeof(TestDbContext),
            firstOutbox);
        using var provider = services.BuildServiceProvider();
        var context = new WolverineModuleExecutionContext(
            provider,
            CreateRegistry());

        context.Enter(typeof(TestCommand));
        context.GetUnitOfWork().ShouldBeSameAs(firstUnitOfWork);
        context.GetOutboxDispatcher().ShouldBeSameAs(firstOutbox);
        context.Enter(typeof(DbContext));
        context.GetUnitOfWork().ShouldBeSameAs(secondUnitOfWork);

        context.Exit(typeof(DbContext));
        context.GetUnitOfWork().ShouldBeSameAs(firstUnitOfWork);
        context.Exit(typeof(TestCommand));
    }

    [Fact(DisplayName = "Execution context rejects invalid module exit order")]
    public void ExecutionContextShouldRejectInvalidModuleExitOrder()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var context = new WolverineModuleExecutionContext(
            provider,
            CreateRegistry());

        var emptyException = Should.Throw<InvalidOperationException>(() =>
            context.Exit(typeof(TestCommand)));

        emptyException.Message.ShouldContain("because no module is active");

        context.Enter(typeof(TestCommand));

        var orderException = Should.Throw<InvalidOperationException>(() =>
            context.Exit(typeof(DbContext)));

        orderException.Message.ShouldContain(
            "while 'PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Messages.TestCommand' is active");
    }

    [Fact(DisplayName = "Execution context reports missing keyed module services")]
    public void ExecutionContextShouldReportMissingKeyedModuleServices()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var context = new WolverineModuleExecutionContext(
            provider,
            CreateRegistry());

        context.Enter(typeof(TestCommand));

        var unitOfWorkException = Should.Throw<InvalidOperationException>(
            context.GetUnitOfWork);
        var outboxException = Should.Throw<InvalidOperationException>(() =>
            context.TryGetOutboxDispatcher(out _));

        unitOfWorkException.Message.ShouldContain(
            "No keyed IUnitOfWork is registered");
        outboxException.Message.ShouldContain(
            "No Wolverine outbox dispatcher is registered");
    }

    [Fact(DisplayName = "Execution context reports that no module is active")]
    public void ExecutionContextShouldReportNoActiveModule()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var context = new WolverineModuleExecutionContext(
            provider,
            CreateRegistry());

        context.TryGetOutboxDispatcher(out _).ShouldBeFalse();
        Should.Throw<InvalidOperationException>(context.GetUnitOfWork)
            .Message.ShouldContain("No Wolverine module is active");
        Should.Throw<InvalidOperationException>(context.GetOutboxDispatcher)
            .Message.ShouldContain("No Wolverine module is active");
    }

    private static WolverineModuleRegistry CreateRegistry()
    {
        return new WolverineModuleConfiguration()
            .AddModule<TestDbContext>(typeof(TestCommand).Assembly)
            .AddModule<SecondTestDbContext>(typeof(DbContext).Assembly)
            .Build();
    }
}
