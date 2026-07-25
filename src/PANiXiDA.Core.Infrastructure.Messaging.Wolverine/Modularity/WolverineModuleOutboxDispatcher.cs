using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

using Microsoft.Extensions.DependencyInjection;

using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;

internal sealed class WolverineModuleOutboxDispatcher(
    WolverineModuleExecutionContext moduleContext,
    IServiceProvider serviceProvider) : IOutboxDispatcher
{
    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        if (moduleContext.TryGetOutboxDispatcher(out var outboxDispatcher))
        {
            await outboxDispatcher.PublishAsync(@event, cancellationToken);
            return;
        }

        var messageContext = serviceProvider
            .GetRequiredService<IMessageContext>();

        await messageContext.PublishAsync(@event);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (moduleContext.TryGetOutboxDispatcher(out var outboxDispatcher))
        {
            return outboxDispatcher.FlushAsync(cancellationToken);
        }

        return Task.CompletedTask;
    }
}
