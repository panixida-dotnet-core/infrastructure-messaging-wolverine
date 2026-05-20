namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class ConstrainedBeforeBehavior<TRequest, TResult> : IBeforeRequestBehavior<TRequest, TResult>
    where TRequest : class, IRequest<TResult>
    where TResult : Result
{
    public Task BeforeAsync(
        TRequest request,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
