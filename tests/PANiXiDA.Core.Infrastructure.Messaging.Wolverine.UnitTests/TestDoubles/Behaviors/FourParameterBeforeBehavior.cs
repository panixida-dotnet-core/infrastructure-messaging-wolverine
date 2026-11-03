namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class FourParameterBeforeBehavior<TRequest, TResult, THandler, TExtra> : IBeforeMessageBehavior<TRequest>
    where TRequest : IRequest<TResult>
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
