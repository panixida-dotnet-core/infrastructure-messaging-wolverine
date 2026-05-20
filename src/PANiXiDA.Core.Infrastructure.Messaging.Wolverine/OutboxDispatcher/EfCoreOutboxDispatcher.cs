using Microsoft.EntityFrameworkCore;

using Wolverine.EntityFrameworkCore;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

/// <summary>
/// Uses <see cref="IDbContextOutbox{TDbContext}"/> to publish messages through the Wolverine EF Core outbox.
/// </summary>
/// <typeparam name="TDbContext">The DbContext type used by the transactional outbox.</typeparam>
/// <param name="outbox">The Wolverine outbox associated with the current DbContext scope.</param>
public sealed class EfCoreOutboxDispatcher<TDbContext>(IDbContextOutbox<TDbContext> outbox)
    : IOutboxDispatcher
    where TDbContext : DbContext
{
    /// <summary>
    /// Adds the specified domain event to the current Wolverine outbox.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type.</typeparam>
    /// <param name="event">The domain event instance to publish.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous publish operation.</returns>
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        await outbox.PublishAsync(@event);
    }

    /// <summary>
    /// Flushes all outgoing messages accumulated in the current Wolverine outbox.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous flush operation.</returns>
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return outbox.FlushOutgoingMessagesAsync();
    }
}
