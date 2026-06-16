using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;

using System.Reflection;

using Wolverine.Runtime;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

internal abstract class RequestMiddlewareFrameBase(
    Type requestType,
    Type closedMiddlewareType,
    bool requiresMessageContext = false) : AsyncFrame
{
    protected readonly Type requestType = requestType;
    protected readonly Type closedMiddlewareType = closedMiddlewareType;
    protected readonly ConstructorInfo constructor = RequestMiddlewareCodeGeneration.ResolveConstructor(closedMiddlewareType);
    protected readonly string uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

    protected Variable requestVariable = null!;
    protected Variable cancellationVariable = null!;
    protected Variable messageContextVariable = null!;
    protected Variable[] constructorVariables = null!;

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

        constructorVariables = RequestMiddlewareCodeGeneration.ResolveConstructorVariables(
            chain,
            constructor);

        foreach (var constructorVariable in constructorVariables)
        {
            yield return constructorVariable;
        }
    }

    protected string BuildMiddlewareVariableName()
    {
        return RequestMiddlewareCodeGeneration.BuildVariableName(
            closedMiddlewareType,
            uniqueSuffix);
    }

    protected string GetMiddlewareTypeName()
    {
        return RequestMiddlewareCodeGeneration.GetCodeTypeName(closedMiddlewareType);
    }

    protected string GetConstructorArguments()
    {
        return string.Join(", ", constructorVariables.Select(x => x.Usage));
    }

    protected string GetFriendlyMiddlewareTypeName()
    {
        return RequestMiddlewareCodeGeneration.GetFriendlyTypeName(closedMiddlewareType);
    }
}
