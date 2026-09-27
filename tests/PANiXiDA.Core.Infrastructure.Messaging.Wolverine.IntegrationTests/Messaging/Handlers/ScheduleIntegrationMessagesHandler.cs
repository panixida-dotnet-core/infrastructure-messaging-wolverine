using PANiXiDA.Core.Application.Messaging.Scheduling;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database.Entities;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Commands;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Events;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Support;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Handlers;

public sealed class ScheduleIntegrationMessagesHandler(
    IntegrationDbContext dbContext,
    IScheduler scheduler) : ICommandHandler<ScheduleIntegrationMessagesCommand, Result>
{
    public async Task<Result> HandleAsync(
        ScheduleIntegrationMessagesCommand command,
        CancellationToken cancellationToken)
    {
        dbContext.Records.Add(new IntegrationRecord { Id = command.RecordId, Name = "scheduled" });
        await dbContext.SaveChangesAsync(cancellationToken);

        await scheduler.ScheduleAtAsync(
            new CreateIntegrationRecordCommand(command.ScheduledRecordId, "delivered"),
            command.DeliverAt,
            cancellationToken);
        await scheduler.ScheduleAtAsync(
            new IntegrationDomainEvent(command.EventId, "delayed event"),
            command.DeliverAt,
            cancellationToken);

        if (command.ThrowAfterScheduling)
        {
            throw new PlannedCommandException();
        }

        return command.ReturnFailure
            ? Result.Failure(Error.Failure("Planned scheduling failure."))
            : Result.Success();
    }
}
