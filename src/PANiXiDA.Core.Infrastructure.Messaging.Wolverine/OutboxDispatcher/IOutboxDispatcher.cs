namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

/// <summary>
/// Publishes outgoing messages through the Wolverine outbox.
/// </summary>
/// <remarks>
/// Implementations can store messages in a transactional outbox and flush them after the current
/// application request completes successfully.
/// </remarks>
public interface IOutboxDispatcher
{
    /// <summary>
    /// Adds the specified domain event to the current outbox.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type.</typeparam>
    /// <param name="event">The domain event instance to publish.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous publish operation.</returns>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;

    /// <summary>
    /// Flushes all outgoing messages accumulated in the current outbox.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous flush operation.</returns>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
