using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Behaviors;

public sealed class EventPipelineAfter<TMessage, TResult, THandler>(IntegrationTestJournal journal)
    : IAfterMessageBehavior<TMessage, TResult>
    where TMessage : DomainEvent
    where TResult : Result
    where THandler : IEventHandler<TMessage>
{
    public Task AfterAsync(TMessage message, TResult result, CancellationToken cancellationToken)
    {
        journal.Add("event.after");
        journal.AfterResult = result;
        journal.MessageHandlerType = typeof(THandler);
        if (journal.FailEventAfter)
        {
            throw new InvalidOperationException("Planned event after behavior failure.");
        }

        return Task.CompletedTask;
    }
}
