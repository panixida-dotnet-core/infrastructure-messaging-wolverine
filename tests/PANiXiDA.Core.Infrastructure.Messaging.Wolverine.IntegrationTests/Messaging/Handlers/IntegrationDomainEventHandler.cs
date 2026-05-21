using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database.Entities;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Events;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Handlers;

public sealed class IntegrationDomainEventHandler(
    IntegrationDbContext dbContext,
    IntegrationTestJournal journal) : IEventHandler<IntegrationDomainEvent>
{
    public async Task HandleAsync(
        IntegrationDomainEvent @event,
        CancellationToken cancellationToken)
    {
        journal.Add("handler.event");

        dbContext.HandledEvents.Add(new HandledEventRecord
        {
            Id = Guid.NewGuid(),
            EventId = @event.EventId,
            Name = @event.Name,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
