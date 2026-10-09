using System.Diagnostics.CodeAnalysis;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

[SuppressMessage("Major Code Smell", "S2326:Unused type parameters should be removed", Justification = "Used to verify middleware validation rejects open generic behavior types with more than three generic parameters.")]
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
