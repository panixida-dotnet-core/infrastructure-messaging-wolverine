namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class ConstrainedBeforeBehavior<TRequest, TResult, THandler> : IBeforeMessageBehavior<TRequest>
    where TRequest : class, IRequest<TResult>
    where THandler : IRequestHandler<TRequest, TResult>
    where TResult : Result
{
    public Task<Result> BeforeAsync(
        TRequest message,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success());
    }
}
