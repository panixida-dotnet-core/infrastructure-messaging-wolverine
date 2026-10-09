namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class ConstrainedBeforeBehavior<TRequest, TResult, THandler> : IBeforeRequestBehavior<TRequest, TResult, THandler>
    where TRequest : class, IRequest<TResult>
    where THandler : PANiXiDA.Core.Application.Messaging.Mediator.Handlers.IRequestHandler<TRequest, TResult>
    where TResult : Result
{
    public Task<Result> BeforeAsync(
        TRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success());
    }
}
