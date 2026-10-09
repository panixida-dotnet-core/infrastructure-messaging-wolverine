using PANiXiDA.Core.Application.Messaging.Mediator.Handlers;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class TestRequestHandler<TRequest, TResult> : IRequestHandler<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result
{
    public Task<TResult> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
