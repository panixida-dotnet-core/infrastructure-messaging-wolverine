using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database.Entities;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Support;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Events;

using Wolverine.Attributes;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Handlers;

[MaximumAttempts(1)]
public sealed class FirstModuleSharedEventHandler(
    IntegrationDbContext dbContext,
    IAggregateTracker aggregateTracker) : IEventHandler<SharedModuleEvent>
{
    public Task HandleAsync(
        SharedModuleEvent @event,
        CancellationToken cancellationToken)
    {
        dbContext.HandledEvents.Add(new HandledEventRecord
        {
            Id = Guid.NewGuid(),
            EventId = @event.EventId,
            Name = @event.FailFirstModuleSave ? null! : @event.Name
        });

        var aggregate = new TestEventAggregate(@event.EventId);
        aggregate.Raise(new SharedModuleFollowUpEvent(@event.EventId));
        aggregateTracker.Track(aggregate);

        if (@event.FailFirstModuleHandler)
        {
            throw new InvalidOperationException("Planned first module handler failure.");
        }

        return Task.CompletedTask;
    }
}
