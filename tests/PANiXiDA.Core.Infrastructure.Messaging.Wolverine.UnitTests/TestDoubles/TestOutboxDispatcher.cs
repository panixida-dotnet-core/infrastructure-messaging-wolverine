using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

public sealed class TestOutboxDispatcher : IOutboxDispatcher
{
    public int PublishCallCount { get; private set; }

    public int FlushCallCount { get; private set; }

    public object? LastPublishedEvent { get; private set; }

    public CancellationToken LastFlushCancellationToken { get; private set; }

    public Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        PublishCallCount++;
        LastPublishedEvent = @event;

        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        FlushCallCount++;
        LastFlushCancellationToken = cancellationToken;

        return Task.CompletedTask;
    }
}
