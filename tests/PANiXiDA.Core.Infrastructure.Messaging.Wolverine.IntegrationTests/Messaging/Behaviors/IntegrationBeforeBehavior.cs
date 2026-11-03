using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Behaviors;

public sealed class IntegrationBeforeBehavior<TRequest, TResult, THandler>(
    IntegrationTestJournal journal) : IBeforeMessageBehavior<TRequest>
    where TRequest : IRequest<TResult>
    where THandler : IRequestHandler<TRequest, TResult>
    where TResult : Result
{
    public Task<Result> BeforeAsync(
        TRequest message,
        CancellationToken cancellationToken)
    {
        journal.Add("behavior.before");

        return Task.FromResult(Result.Success());
    }
}
