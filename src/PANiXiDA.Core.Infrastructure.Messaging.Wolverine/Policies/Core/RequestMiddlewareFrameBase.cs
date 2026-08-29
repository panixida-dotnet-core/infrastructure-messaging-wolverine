using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using JasperFx.CodeGeneration;

using Wolverine.Runtime;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

internal abstract class RequestMiddlewareFrameBase(
    Type requestType,
    IReadOnlyList<RequestMiddlewareDescriptor> descriptors,
    bool requiresMessageContext = false) : AsyncFrame
{
    protected readonly IReadOnlyList<RequestMiddlewareDescriptor> middlewareDescriptors = descriptors;

    protected Variable requestVariable = null!;
    protected Variable cancellationVariable = null!;
    protected Variable messageContextVariable = null!;

    public sealed override IEnumerable<Variable> FindVariables(IMethodVariables chain)
    {
        requestVariable = chain.FindVariable(requestType);
        yield return requestVariable;

        cancellationVariable = chain.FindVariable(typeof(CancellationToken));
        yield return cancellationVariable;

        if (requiresMessageContext)
        {
            messageContextVariable = chain.FindVariable(typeof(MessageContext));
            yield return messageContextVariable;
        }

        foreach (var middleware in middlewareDescriptors)
        {
            foreach (var variable in middleware.ResolveVariables(chain))
            {
                yield return variable;
            }
        }
    }

    protected void GenerateNextFrame(
        GeneratedMethod method,
        ISourceWriter writer)
    {
        Next?.GenerateCode(method, writer);
    }
}
