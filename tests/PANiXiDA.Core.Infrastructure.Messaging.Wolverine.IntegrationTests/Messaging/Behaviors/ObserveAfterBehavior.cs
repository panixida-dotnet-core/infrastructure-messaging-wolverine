using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Behaviors;

public sealed class ObserveAfterBehavior<TRequest, TResult>(IntegrationTestJournal journal)
    : IAfterMessageBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result
{
    public Task AfterAsync(TRequest message, TResult result, CancellationToken cancellationToken)
    {
        journal.Add("behavior.after");
        journal.AfterResult = result;
        return Task.CompletedTask;
    }
}
