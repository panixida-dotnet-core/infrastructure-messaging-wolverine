using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

internal abstract class RequestMiddlewareFrameBase(
    Type requestType,
    IReadOnlyList<RequestMiddlewareDescriptor> descriptors) : AsyncFrame
{
    protected readonly IReadOnlyList<RequestMiddlewareDescriptor> middlewareDescriptors = descriptors;

    protected Variable requestVariable = null!;
    protected Variable cancellationVariable = null!;

    public sealed override IEnumerable<Variable> FindVariables(IMethodVariables chain)
    {
        requestVariable = chain.FindVariable(requestType);
        yield return requestVariable;

        cancellationVariable = chain.FindVariable(typeof(CancellationToken));
        yield return cancellationVariable;

        foreach (var middleware in middlewareDescriptors)
        {
            foreach (var variable in middleware.ResolveVariables(chain))
            {
                yield return variable;
            }
        }
    }
}
