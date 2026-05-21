using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests;

public sealed class WolverineEventBusTests
{
    [Fact(DisplayName = "PublishAsync delegates domain event to outbox dispatcher")]
    public async Task PublishAsyncShouldDelegateDomainEventToOutboxDispatcher()
    {
        var outboxDispatcher = new TestOutboxDispatcher();
        var eventBus = new WolverineEventBus(outboxDispatcher);
        var domainEvent = new TestDomainEvent(Guid.NewGuid());

        await eventBus.PublishAsync(
            domainEvent,
            TestContext.Current.CancellationToken);

        outboxDispatcher.PublishCallCount.Should().Be(1);
        outboxDispatcher.LastPublishedEvent.Should().BeSameAs(domainEvent);
    }
}
