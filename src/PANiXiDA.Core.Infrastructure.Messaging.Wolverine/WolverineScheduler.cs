using PANiXiDA.Core.Application.Messaging.Scheduling;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine;

/// <summary>
/// Schedules commands and domain events through a Wolverine transactional outbox.
/// </summary>
/// <remarks>
/// Cancellation support depends on the dispatcher. The built-in Wolverine dispatchers do not cancel scheduling operations.
/// </remarks>
/// <param name="outboxDispatcher">The dispatcher used to schedule messages through the current outbox.</param>
public sealed class WolverineScheduler(IOutboxDispatcher outboxDispatcher) : IScheduler
{
    /// <inheritdoc />
    public Task ScheduleAsync(
        ICommand<Result> command,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        return outboxDispatcher.SendAsync(command, new DeliveryOptions { ScheduleDelay = delay }, cancellationToken);
    }

    /// <inheritdoc />
    public Task ScheduleAsync(
        DomainEvent @event,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        return outboxDispatcher.PublishAsync(@event, new DeliveryOptions { ScheduleDelay = delay }, cancellationToken);
    }

    /// <inheritdoc />
    public Task ScheduleAtAsync(
        ICommand<Result> command,
        DateTimeOffset deliverAt,
        CancellationToken cancellationToken)
    {
        return outboxDispatcher.SendAsync(command, new DeliveryOptions { ScheduledTime = deliverAt }, cancellationToken);
    }

    /// <inheritdoc />
    public Task ScheduleAtAsync(
        DomainEvent @event,
        DateTimeOffset deliverAt,
        CancellationToken cancellationToken)
    {
        return outboxDispatcher.PublishAsync(@event, new DeliveryOptions { ScheduledTime = deliverAt }, cancellationToken);
    }
}
