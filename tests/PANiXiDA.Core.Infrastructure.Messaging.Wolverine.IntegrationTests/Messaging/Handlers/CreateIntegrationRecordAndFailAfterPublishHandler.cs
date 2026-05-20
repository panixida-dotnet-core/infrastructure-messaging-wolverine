using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database.Entities;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Commands;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Events;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Support;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Handlers;

public sealed class CreateIntegrationRecordAndFailAfterPublishHandler(
    IntegrationDbContext dbContext,
    IEventBus eventBus,
    IntegrationTestJournal journal) : ICommandHandler<CreateIntegrationRecordAndFailAfterPublishCommand, Result>
{
    public async Task<Result> HandleAsync(
        CreateIntegrationRecordAndFailAfterPublishCommand command,
        CancellationToken cancellationToken)
    {
        journal.Add("handler.command");

        dbContext.Records.Add(new IntegrationRecord
        {
            Id = command.Id,
            Name = command.Name,
        });

        await eventBus.PublishAsync(
            new IntegrationDomainEvent(command.Id, command.Name),
            cancellationToken);

        throw new PlannedCommandException();
    }
}
