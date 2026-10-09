namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class FourParameterBeforeBehavior<TRequest, TResult, THandler, TExtra> : IBeforeRequestBehavior<TRequest, TResult, THandler>
    where TRequest : IRequest<TResult>
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
