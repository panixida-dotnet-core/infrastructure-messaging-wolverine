using Microsoft.EntityFrameworkCore;

using Wolverine;
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

    /// <inheritdoc />
    public Task SendAsync(ICommand<Result> command, DeliveryOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        return outbox.SendAsync(command, options).AsTask();
    }

    /// <inheritdoc />
    public Task PublishAsync<TEvent>(TEvent @event, DeliveryOptions options, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        return outbox.PublishAsync(@event, options).AsTask();
    }

    /// <summary>
    /// Saves tracked changes, including scheduled envelopes, without committing the transaction.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel persistence.</param>
    /// <returns>A task that represents the asynchronous save operation.</returns>
    public Task PersistAsync(CancellationToken cancellationToken)
    {
        return outbox.DbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Flushes all outgoing messages accumulated in the current Wolverine outbox.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous flush operation.</returns>
    public Task FlushAsync(CancellationToken cancellationToken)
    {
        return outbox.FlushOutgoingMessagesAsync();
    }
}
