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

    public Task PublishAsync<TEvent>(TEvent @event, DeliveryOptions options, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        if (moduleContext.TryGetOutboxDispatcher(out var outboxDispatcher))
        {
            return outboxDispatcher.PublishAsync(@event, options, cancellationToken);
        }

        return serviceProvider.GetRequiredService<IMessageContext>().PublishAsync(@event, options).AsTask();
    }

    public Task SendAsync(ICommand<Result> command, DeliveryOptions options, CancellationToken cancellationToken)
    {
        if (moduleContext.TryGetOutboxDispatcher(out var outboxDispatcher))
        {
            return outboxDispatcher.SendAsync(command, options, cancellationToken);
        }

        return serviceProvider.GetRequiredService<IMessageContext>().SendAsync(command, options).AsTask();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (moduleContext.TryGetOutboxDispatcher(out var outboxDispatcher))
        {
            return outboxDispatcher.SaveChangesAsync(cancellationToken);
        }

        throw new InvalidOperationException(
            "No Wolverine module is active in the current request scope. " +
            "Use the keyed module outbox dispatcher to save changes outside the mediator pipeline.");
    }

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        if (moduleContext.TryGetOutboxDispatcher(out var outboxDispatcher))
        {
            return outboxDispatcher.FlushAsync(cancellationToken);
        }

        return Task.CompletedTask;
    }
}
