using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Behaviors;

public sealed class IntegrationBeforeBehavior<TRequest, TResult>(
    IntegrationTestJournal journal) : IBeforeRequestBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result
{
    public Task BeforeAsync(
        TRequest request,
        CancellationToken cancellationToken)
    {
        journal.Add("behavior.before");

        return Task.CompletedTask;
    }
}
