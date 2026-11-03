using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Behaviors;

public sealed class EventPipelineBefore<TMessage>(IntegrationTestJournal journal) : IBeforeMessageBehavior<TMessage>
    where TMessage : DomainEvent
{
    public Task<Result> BeforeAsync(TMessage message, CancellationToken cancellationToken)
    {
        journal.Add("event.before");
        return Task.FromResult(journal.RejectEvents
            ? Result.Failure(Error.Validation("Event rejected by behavior."))
            : Result.Success());
    }
}
