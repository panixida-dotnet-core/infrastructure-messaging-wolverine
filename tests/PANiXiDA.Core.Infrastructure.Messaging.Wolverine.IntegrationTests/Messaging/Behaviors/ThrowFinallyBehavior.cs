using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Support;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Behaviors;

public sealed class ThrowFinallyBehavior<TRequest, TResult>(IntegrationTestJournal journal)
    : IFinallyRequestBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result
{
    public Task FinallyAsync(TRequest request, TResult? result, Exception? exception, CancellationToken cancellationToken)
    {
        journal.Add("behavior.throwFinally");
        throw new PlannedCommandException();
    }
}
