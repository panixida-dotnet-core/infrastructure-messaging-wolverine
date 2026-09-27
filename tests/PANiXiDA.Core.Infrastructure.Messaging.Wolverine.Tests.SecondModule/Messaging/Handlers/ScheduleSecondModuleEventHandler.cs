using PANiXiDA.Core.Application.Messaging.Scheduling;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Database.Entities;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Commands;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Events;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Handlers;

public sealed class ScheduleSecondModuleEventHandler(
    SecondModuleDbContext dbContext,
    IScheduler scheduler) : IEventHandler<ScheduleSecondModuleEvent>
{
    public async Task HandleAsync(ScheduleSecondModuleEvent @event, CancellationToken cancellationToken)
    {
        dbContext.Records.Add(new SecondModuleRecord { Id = @event.EventId, Name = "native handler" });

        await scheduler.ScheduleAsync(
            new CreateSecondModuleRecordCommand(Guid.NewGuid(), "scheduled"),
            TimeSpan.FromMinutes(15),
            cancellationToken);
        await scheduler.ScheduleAsync(
            new SharedModuleFollowUpEvent(@event.EventId),
            TimeSpan.FromMinutes(15),
            cancellationToken);

        if (@event.Fail)
        {
            throw new PlannedSecondModuleException();
        }
    }
}
