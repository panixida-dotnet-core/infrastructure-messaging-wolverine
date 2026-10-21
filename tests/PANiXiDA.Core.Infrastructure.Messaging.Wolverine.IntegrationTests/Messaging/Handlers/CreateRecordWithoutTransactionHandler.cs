using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database.Entities;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Commands;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Events;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Handlers;

public sealed class CreateRecordWithoutTransactionHandler(
    IntegrationDbContext dbContext,
    IEventBus eventBus,
    IntegrationTestJournal journal) : ICommandHandler<CreateRecordWithoutTransactionCommand, Result>
{
    public async Task<Result> HandleAsync(CreateRecordWithoutTransactionCommand command, CancellationToken cancellationToken)
    {
        journal.Add("handler.command");
        dbContext.Records.Add(new IntegrationRecord { Id = command.Id, Name = "without-transaction" });
        if (command.SaveImmediately)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (command.Fail)
        {
            return Result.Failure(Error.Failure("Planned failure."));
        }

        await eventBus.PublishAsync(new IntegrationDomainEvent(command.Id, "without-transaction"), cancellationToken);
        return Result.Success();
    }
}
