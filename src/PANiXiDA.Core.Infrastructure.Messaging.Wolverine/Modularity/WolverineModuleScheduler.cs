using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Application.Messaging.Scheduling;

using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;

internal sealed class WolverineModuleScheduler(
    WolverineModuleExecutionContext moduleContext,
    IServiceProvider serviceProvider) : IScheduler
{
    public Task ScheduleAsync(
        ICommand<Result> command,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        return GetScheduler().ScheduleAsync(command, delay, cancellationToken);
    }

    public Task ScheduleAsync(
        DomainEvent @event,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        return GetScheduler().ScheduleAsync(@event, delay, cancellationToken);
    }

    public Task ScheduleAtAsync(
        ICommand<Result> command,
        DateTimeOffset deliverAt,
        CancellationToken cancellationToken)
    {
        return GetScheduler().ScheduleAtAsync(command, deliverAt, cancellationToken);
    }

    public Task ScheduleAtAsync(
        DomainEvent @event,
        DateTimeOffset deliverAt,
        CancellationToken cancellationToken)
    {
        return GetScheduler().ScheduleAtAsync(@event, deliverAt, cancellationToken);
    }

    private IScheduler GetScheduler()
    {
        return moduleContext.TryGetScheduler(out var scheduler)
            ? scheduler
            : new WolverineScheduler(serviceProvider.GetRequiredService<IMessageContext>());
    }
}
