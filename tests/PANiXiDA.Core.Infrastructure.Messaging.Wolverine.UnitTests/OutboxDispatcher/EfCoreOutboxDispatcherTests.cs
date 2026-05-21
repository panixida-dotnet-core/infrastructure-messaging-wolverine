using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.OutboxDispatcher;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.OutboxDispatcher;

public sealed class EfCoreOutboxDispatcherTests
{
    [Fact(DisplayName = "PublishAsync delegates event to EF Core outbox")]
    public async Task PublishAsyncShouldDelegateEventToEfCoreOutbox()
    {
        var outbox = DbContextOutboxProxy<TestDbContext>.Create(out var proxy);
        var dispatcher = new EfCoreOutboxDispatcher<TestDbContext>(outbox);
        var domainEvent = new TestDomainEvent(Guid.NewGuid());

        await dispatcher.PublishAsync(
            domainEvent,
            TestContext.Current.CancellationToken);

        proxy.PublishCallCount.Should().Be(1);
        proxy.LastPublishedMessage.Should().BeSameAs(domainEvent);
    }

    [Fact(DisplayName = "FlushAsync delegates flush to EF Core outbox")]
    public async Task FlushAsyncShouldDelegateFlushToEfCoreOutbox()
    {
        var outbox = DbContextOutboxProxy<TestDbContext>.Create(out var proxy);
        var dispatcher = new EfCoreOutboxDispatcher<TestDbContext>(outbox);

        await dispatcher.FlushAsync(TestContext.Current.CancellationToken);

        proxy.FlushCallCount.Should().Be(1);
    }
}
