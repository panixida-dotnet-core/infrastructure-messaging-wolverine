using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.OutboxDispatcher;

public sealed class OutboxDispatcherCompatibilityTests
{
    [Fact(DisplayName = "Existing outbox implementations can use default persistence without flushing")]
    public async Task ExistingOutboxShouldSupportDefaultPersistence()
    {
        var legacyOutbox = new LegacyOutboxDispatcher();
        IOutboxDispatcher outbox = legacyOutbox;

        await outbox.PersistAsync(TestContext.Current.CancellationToken);

        legacyOutbox.PublishCallCount.ShouldBe(0);
        legacyOutbox.FlushCallCount.ShouldBe(0);
    }

    private sealed class LegacyOutboxDispatcher : IOutboxDispatcher
    {
        public int PublishCallCount { get; private set; }

        public int FlushCallCount { get; private set; }

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
        {
            PublishCallCount++;
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            FlushCallCount++;
            return Task.CompletedTask;
        }
    }
}
