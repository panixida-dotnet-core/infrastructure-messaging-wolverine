using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database.Entities;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Events;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Handlers;

public sealed class FirstModuleSharedEventHandler(
    IntegrationDbContext dbContext,
    IEventBus eventBus) : IEventHandler<SharedModuleEvent>
{
    public async Task HandleAsync(
        SharedModuleEvent @event,
        CancellationToken cancellationToken)
    {
        dbContext.HandledEvents.Add(new HandledEventRecord
        {
            Id = Guid.NewGuid(),
            EventId = @event.EventId,
            Name = @event.Name
        });

        await eventBus.PublishAsync(
            new SharedModuleFollowUpEvent(@event.EventId),
            cancellationToken);
    }
}
