using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Behaviors;

public sealed class IntegrationBeforeBehavior<TRequest, TResult, THandler>(
    IntegrationTestJournal journal) : IBeforeRequestBehavior<TRequest, TResult, THandler>
    where TRequest : IRequest<TResult>
    where THandler : PANiXiDA.Core.Application.Messaging.Mediator.Handlers.IRequestHandler<TRequest, TResult>
    where TResult : Result
{
    public Task<Result> BeforeAsync(
        TRequest request,
        CancellationToken cancellationToken)
    {
        journal.Add("behavior.before");

        return Task.FromResult(Result.Success());
    }
}
