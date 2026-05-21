namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class SecondBeforeBehavior<TRequest, TResult> : IBeforeRequestBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result
{
    public Task BeforeAsync(
        TRequest request,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
