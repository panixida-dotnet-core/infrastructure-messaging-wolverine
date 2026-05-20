using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine;

internal sealed class WolverineEventBus(
    IOutboxDispatcher outboxDispatcher) : IEventBus
{
    public Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : DomainEvent
    {
        return outboxDispatcher.PublishAsync(@event, cancellationToken);
    }
}
