using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database.Entities;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Events;

using Wolverine.Attributes;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Handlers;

[Transactional]
[MaximumAttempts(1)]
public sealed class PipelineEventHandler(IntegrationDbContext dbContext, IntegrationTestJournal journal)
    : IEventHandler<PipelineEvent>
{
    public Task HandleAsync(PipelineEvent @event, CancellationToken cancellationToken)
    {
        journal.Add("event.handler");
        dbContext.HandledEvents.Add(new HandledEventRecord
        {
            Id = Guid.NewGuid(),
            EventId = @event.EventId,
            Name = "pipeline"
        });

        if (@event.ThrowHandler)
        {
            throw new InvalidOperationException("Planned event handler failure.");
        }

        return Task.CompletedTask;
    }
}
