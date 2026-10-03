using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

/// <summary>
/// Sends and publishes outgoing messages through the Wolverine outbox.
/// </summary>
/// <remarks>
/// Implementations can store messages in a transactional outbox and flush them after the current
/// application request completes successfully.
/// The built-in Wolverine adapters do not support cancellation when sending, publishing, or flushing messages.
/// </remarks>
public interface IOutboxDispatcher
{
    /// <summary>
    /// Adds the specified domain event to the current outbox.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type.</typeparam>
    /// <param name="event">The domain event instance to publish.</param>
    /// <param name="cancellationToken">The caller's cancellation token; support depends on the implementation.</param>
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
    /// Saves pending outbox messages before committing the current transaction.
    /// </summary>
    /// <remarks>
    /// EF Core implementations also save other tracked changes in the same DbContext.
    /// Implementations that persist messages immediately can return a completed task.
    /// This operation must not commit the transaction or release messages for delivery.
    /// The modular dispatcher requires an active mediator module. Outside the mediator pipeline,
    /// use the keyed dispatcher for the required module; native handlers rely on Wolverine's transaction middleware.
    /// </remarks>
    /// <param name="cancellationToken">The token used to cancel saving changes.</param>
    /// <returns>A task that represents the asynchronous save operation.</returns>
    /// <exception cref="InvalidOperationException">
    /// No mediator module is active when saving through the unkeyed modular dispatcher.
    /// </exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Flushes all outgoing messages accumulated in the current outbox.
    /// </summary>
    /// <remarks>
    /// Without an active mediator module, the modular dispatcher leaves flushing to Wolverine's transaction middleware.
    /// </remarks>
    /// <param name="cancellationToken">The caller's cancellation token; support depends on the implementation.</param>
    /// <returns>A task that represents the asynchronous flush operation.</returns>
    Task FlushAsync(CancellationToken cancellationToken);
}
