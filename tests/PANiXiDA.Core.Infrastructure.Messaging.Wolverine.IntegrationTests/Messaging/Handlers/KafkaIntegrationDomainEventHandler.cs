using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database.Entities;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Events;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Handlers;

public sealed class KafkaIntegrationDomainEventHandler(
    IntegrationDbContext dbContext,
    IntegrationTestJournal journal) : IEventHandler<KafkaIntegrationDomainEvent>
{
    public async Task HandleAsync(
        KafkaIntegrationDomainEvent @event,
        CancellationToken cancellationToken)
    {
        journal.Add("handler.kafkaEvent");

        dbContext.HandledEvents.Add(new HandledEventRecord
        {
            Id = Guid.NewGuid(),
            EventId = @event.EventId,
            Name = @event.Name,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
