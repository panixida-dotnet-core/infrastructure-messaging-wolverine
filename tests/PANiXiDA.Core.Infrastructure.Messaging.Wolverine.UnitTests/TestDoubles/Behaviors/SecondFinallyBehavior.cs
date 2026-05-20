namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class SecondFinallyBehavior<TRequest, TResult> : IFinallyRequestBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result
{
    public Task FinallyAsync(
        TRequest request,
        TResult? result,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
