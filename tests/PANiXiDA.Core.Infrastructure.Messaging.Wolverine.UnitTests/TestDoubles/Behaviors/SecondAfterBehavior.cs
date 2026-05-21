namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class SecondAfterBehavior<TRequest, TResult> : IAfterRequestBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result
{
    public Task AfterAsync(
        TRequest request,
        TResult result,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
