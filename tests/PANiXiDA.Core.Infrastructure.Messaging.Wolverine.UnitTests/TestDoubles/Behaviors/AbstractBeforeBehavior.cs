namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public abstract class AbstractBeforeBehavior<TRequest, TResult, THandler> : IBeforeRequestBehavior<TRequest, TResult, THandler>
    where TRequest : IRequest<TResult>
    where THandler : IRequestHandler<TRequest, TResult>
    where TResult : Result
{
    public abstract Task<Result> BeforeAsync(
        TRequest request,
        CancellationToken cancellationToken);
}
