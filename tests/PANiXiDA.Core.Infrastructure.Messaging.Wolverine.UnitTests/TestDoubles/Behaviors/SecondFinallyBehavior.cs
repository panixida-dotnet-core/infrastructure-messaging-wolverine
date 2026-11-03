namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class SecondFinallyBehavior<TRequest, TResult> : IFinallyMessageBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result
{
    public Task<Result?> FinallyAsync(
        TRequest message,
        TResult? result,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<Result?>(result);
    }
}
