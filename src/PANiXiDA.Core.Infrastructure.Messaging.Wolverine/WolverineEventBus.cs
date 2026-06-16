using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine;

/// <summary>
/// Wolverine outbox-backed implementation of the PANiXiDA event bus abstraction.
/// </summary>
/// <param name="outboxDispatcher">The dispatcher used to publish events through the Wolverine outbox.</param>
public sealed class WolverineEventBus(
    IOutboxDispatcher outboxDispatcher) : IEventBus
{
    /// <inheritdoc />
    public Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : DomainEvent
    {
        return outboxDispatcher.PublishAsync(@event, cancellationToken);
    }
}
