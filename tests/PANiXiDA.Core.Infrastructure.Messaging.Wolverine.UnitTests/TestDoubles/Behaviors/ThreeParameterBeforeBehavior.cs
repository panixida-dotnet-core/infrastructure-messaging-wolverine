using System.Diagnostics.CodeAnalysis;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

[SuppressMessage("Major Code Smell", "S2326:Unused type parameters should be removed", Justification = "Used to verify middleware validation rejects open generic behavior types with more than two generic parameters.")]
public sealed class ThreeParameterBeforeBehavior<TRequest, TResult, TExtra> : IBeforeRequestBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result
{
    public Task BeforeAsync(
        TRequest request,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
