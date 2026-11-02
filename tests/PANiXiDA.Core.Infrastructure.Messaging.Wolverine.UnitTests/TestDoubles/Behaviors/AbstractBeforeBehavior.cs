namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public abstract class AbstractBeforeBehavior<TRequest, TResult, THandler> : IBeforeMessageBehavior<TRequest>
    where TRequest : IRequest<TResult>
    where THandler : IRequestHandler<TRequest, TResult>
    where TResult : Result
{
    public abstract Task<Result> BeforeAsync(
        TRequest message,
        CancellationToken cancellationToken);
}
