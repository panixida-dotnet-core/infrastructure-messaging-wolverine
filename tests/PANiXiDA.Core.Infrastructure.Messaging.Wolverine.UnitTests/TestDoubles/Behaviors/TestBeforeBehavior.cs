namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class TestBeforeBehavior<TRequest, TResult, THandler> : IBeforeRequestBehavior<TRequest, TResult, THandler>
    where TRequest : IRequest<TResult>
    where THandler : IRequestHandler<TRequest, TResult>
    where TResult : Result
{
    public Task<Result> BeforeAsync(
        TRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success());
    }
}
