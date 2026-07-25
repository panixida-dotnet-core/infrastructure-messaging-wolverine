using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Database.Entities;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Events;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Handlers;

public sealed class SecondModuleFollowUpEventHandler(
    SecondModuleDbContext dbContext) : IEventHandler<SharedModuleFollowUpEvent>
{
    public Task HandleAsync(
        SharedModuleFollowUpEvent @event,
        CancellationToken cancellationToken)
    {
        dbContext.HandledEvents.Add(new SecondModuleHandledEvent
        {
            Id = Guid.NewGuid(),
            EventId = @event.EventId,
            EventType = nameof(SharedModuleFollowUpEvent)
        });

        return Task.CompletedTask;
    }
}
