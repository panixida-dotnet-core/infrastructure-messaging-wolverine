using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Behaviors;

public sealed class ObserveFinallyBehavior<TRequest, TResult>(IntegrationTestJournal journal)
    : IFinallyRequestBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result
{
    public Task FinallyAsync(TRequest request, TResult? result, Exception? exception, CancellationToken cancellationToken)
    {
        journal.Add("behavior.finally");
        journal.FinallyResult = result;
        journal.FinallyException = exception;
        return Task.CompletedTask;
    }
}
