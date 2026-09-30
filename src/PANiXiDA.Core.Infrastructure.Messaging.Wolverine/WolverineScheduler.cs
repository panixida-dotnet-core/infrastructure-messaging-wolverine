using PANiXiDA.Core.Application.Messaging.Scheduling;

using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine;

/// <summary>
/// Schedules commands and domain events through a Wolverine transactional outbox.
/// </summary>
/// <param name="outbox">The EF Core outbox or enlisted message context for the current transaction.</param>
public class WolverineScheduler(IMessageBus outbox) : IScheduler
{
    /// <inheritdoc />
    public Task ScheduleAsync(
        ICommand<Result> command,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        return outbox.SendAsync(command, new DeliveryOptions { ScheduleDelay = delay }).AsTask();
    }

    /// <inheritdoc />
    public Task ScheduleAsync(
        DomainEvent @event,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        return outbox.PublishAsync(@event, new DeliveryOptions { ScheduleDelay = delay }).AsTask();
    }

    /// <inheritdoc />
    public Task ScheduleAtAsync(
        ICommand<Result> command,
        DateTimeOffset deliverAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return outbox.SendAsync(command, new DeliveryOptions { ScheduledTime = deliverAt }).AsTask();
    }

    /// <inheritdoc />
    public Task ScheduleAtAsync(
        DomainEvent @event,
        DateTimeOffset deliverAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        cancellationToken.ThrowIfCancellationRequested();

        return outbox.PublishAsync(@event, new DeliveryOptions { ScheduledTime = deliverAt }).AsTask();
    }
}
