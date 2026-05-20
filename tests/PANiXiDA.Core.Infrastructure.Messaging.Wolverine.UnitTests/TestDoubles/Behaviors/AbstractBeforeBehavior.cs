namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public abstract class AbstractBeforeBehavior<TRequest, TResult> : IBeforeRequestBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result
{
    public abstract Task BeforeAsync(
        TRequest request,
        CancellationToken cancellationToken);
}
