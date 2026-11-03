namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class TestAfterBehavior<TRequest, TResult> : IAfterMessageBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result
{
    public Task AfterAsync(
        TRequest message,
        TResult result,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
