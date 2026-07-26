using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Database.Entities;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Events;

using Wolverine.Attributes;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Handlers;

[MaximumAttempts(1)]
public sealed class SecondModuleSharedEventHandler(
    SecondModuleDbContext dbContext) : IEventHandler<SharedModuleEvent>
{
    public Task HandleAsync(
        SharedModuleEvent @event,
        CancellationToken cancellationToken)
    {
        dbContext.HandledEvents.Add(new SecondModuleHandledEvent
        {
            Id = Guid.NewGuid(),
            EventId = @event.EventId,
            EventType = nameof(SharedModuleEvent)
        });

        if (@event.FailSecondModuleHandler)
        {
            throw new PlannedSecondModuleException();
        }

        return Task.CompletedTask;
    }
}
