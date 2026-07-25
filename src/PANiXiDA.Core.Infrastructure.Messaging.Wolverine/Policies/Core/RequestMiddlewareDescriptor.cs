using JasperFx.CodeGeneration.Model;

using System.Reflection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

internal sealed class RequestMiddlewareDescriptor(Type type)
{
    private readonly ConstructorInfo constructor =
        RequestMiddlewareCodeGeneration.ResolveConstructor(type);

    private Variable[] constructorVariables = [];

    internal Type Type { get; } = type;

    internal string UniqueSuffix { get; } =
        Guid.NewGuid().ToString("N")[..8];

    internal static RequestMiddlewareDescriptor[] Resolve(
        Type requestType,
        Type resultType,
        Type behaviorInterfaceType,
        IReadOnlyList<Type> middlewareTypes)
    {
        return middlewareTypes
            .Select(middlewareType => TryCreate(
                requestType,
                resultType,
                behaviorInterfaceType,
                middlewareType))
            .OfType<RequestMiddlewareDescriptor>()
            .ToArray();
    }

    internal IEnumerable<Variable> ResolveVariables(
        IMethodVariables chain)
    {
        constructorVariables =
            RequestMiddlewareCodeGeneration.ResolveConstructorVariables(
                chain,
                constructor);

        return constructorVariables;
    }

    internal string BuildVariableName()
    {
        return RequestMiddlewareCodeGeneration.BuildVariableName(
            Type,
            UniqueSuffix);
    }

    internal string GetTypeName()
    {
        return RequestMiddlewareCodeGeneration.GetCodeTypeName(Type);
    }

    internal string GetConstructorArguments()
    {
        return string.Join(
            ", ",
            constructorVariables.Select(variable => variable.Usage));
    }

    internal string GetFriendlyTypeName()
    {
        return RequestMiddlewareCodeGeneration.GetFriendlyTypeName(Type);
    }

    private static RequestMiddlewareDescriptor? TryCreate(
        Type requestType,
        Type resultType,
        Type behaviorInterfaceType,
        Type middlewareType)
    {
        if (!RequestMiddlewareCodeGeneration.TryResolveClosedMiddlewareType(
                middlewareType,
                requestType,
                resultType,
                behaviorInterfaceType,
                out var closedMiddlewareType))
        {
            return null;
        }

        return new RequestMiddlewareDescriptor(closedMiddlewareType);
    }
}
