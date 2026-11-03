using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Behaviors;

public sealed class EventPipelineFinally<TMessage, TResult>(IntegrationTestJournal journal)
    : IFinallyMessageBehavior<TMessage, TResult>
    where TMessage : DomainEvent
    where TResult : Result
{
    public Task<Result?> FinallyAsync(TMessage message, TResult? result, Exception? exception, CancellationToken cancellationToken)
    {
        journal.Add("event.finally");
        journal.FinallyResult = result;
        journal.FinallyException = exception;
        return Task.FromResult<Result?>(result);
    }
}
