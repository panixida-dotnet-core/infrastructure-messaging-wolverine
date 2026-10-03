using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

/// <summary>
/// Sends and publishes outgoing messages through the Wolverine outbox.
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
    /// Adds a domain event with the specified delivery options to the current outbox.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type.</typeparam>
    /// <param name="event">The domain event instance to publish.</param>
    /// <param name="options">The delivery options, including any scheduling delay or time.</param>
    /// <param name="cancellationToken">The caller's cancellation token; support depends on the implementation.</param>
    /// <returns>A task that represents the asynchronous publish operation.</returns>
    Task PublishAsync<TEvent>(TEvent @event, DeliveryOptions options, CancellationToken cancellationToken)
        where TEvent : IDomainEvent;

    /// <summary>
    /// Adds a command with the specified delivery options to the current outbox.
    /// </summary>
    /// <param name="command">The command to send.</param>
    /// <param name="options">The delivery options, including any scheduling delay or time.</param>
    /// <param name="cancellationToken">The caller's cancellation token; support depends on the implementation.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    Task SendAsync(ICommand<Result> command, DeliveryOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Persists pending outbox messages before committing the current transaction.
    /// </summary>
    /// <remarks>
    /// EF Core implementations also save other tracked changes in the same DbContext.
    /// Implementations that persist messages immediately can return a completed task.
    /// This operation must not commit the transaction or release messages for delivery.
    /// </remarks>
    /// <param name="cancellationToken">The token used to cancel persistence.</param>
    /// <returns>A task that represents the asynchronous persistence operation.</returns>
    Task PersistAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Flushes all outgoing messages accumulated in the current outbox.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous flush operation.</returns>
    Task FlushAsync(CancellationToken cancellationToken);
}
