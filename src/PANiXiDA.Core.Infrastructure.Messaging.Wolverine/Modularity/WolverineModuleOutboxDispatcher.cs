using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;

internal sealed class WolverineModuleOutboxDispatcher(
    WolverineModuleExecutionContext moduleContext) : IOutboxDispatcher
{
    public Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        return moduleContext
            .GetOutboxDispatcher()
            .PublishAsync(@event, cancellationToken);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return moduleContext
            .GetOutboxDispatcher()
            .FlushAsync(cancellationToken);
    }
}
