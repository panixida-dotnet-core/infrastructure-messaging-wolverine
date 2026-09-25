using JasperFx.CodeGeneration.Model;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

internal static class RequestMiddlewareCodeGeneration
{
    internal static bool TryResolveClosedMiddlewareType(
        Type middlewareType,
        Type requestType,
        Type resultType,
        Type behaviorInterfaceType,
        out Type closedMiddlewareType)
    {
        if (!middlewareType.IsGenericTypeDefinition && middlewareType.ContainsGenericParameters)
        {
            closedMiddlewareType = null!;
            return false;
        }

        if (middlewareType.IsGenericTypeDefinition && middlewareType.GetGenericArguments().Length != 2)
        {
            throw new InvalidOperationException(
                $"Open generic middleware '{middlewareType.FullName}' must have exactly 2 generic parameters.");
        }

        return RequestBehaviorMetadata.TryResolve(
            middlewareType, requestType, resultType, behaviorInterfaceType, out closedMiddlewareType);
    }

    internal static IReadOnlyList<Type> ResolveConstructor(Type middlewareType)
    {
        var metadata = RequestBehaviorMetadata.GetBehavior(middlewareType);

        if (metadata.PublicConstructorCount != 1)
        {
            throw new InvalidOperationException(
                $"Type '{middlewareType.FullName}' must have exactly one public constructor.");
        }

        return metadata.ConstructorParameters;
    }

    internal static Variable[] ResolveConstructorVariables(
        IMethodVariables chain,
        IReadOnlyList<Type> parameters)
    {
        var variables = new Variable[parameters.Count];

        for (var i = 0; i < parameters.Count; i++)
        {
            variables[i] = chain.FindVariable(parameters[i]);
        }

        return variables;
    }

    internal static string BuildVariableName(Type type, string uniqueSuffix)
    {
        var friendlyTypeName = GetFriendlyTypeName(type);
        return $"{ToCamelCase(friendlyTypeName)}_{uniqueSuffix}";
    }

    internal static string GetFriendlyTypeName(Type type)
    {
        var name = type.Name;
        var backtickIndex = name.IndexOf('`');
        if (backtickIndex >= 0)
        {
            name = name[..backtickIndex];
        }

        return name;
    }

    internal static string GetCodeTypeName(Type type)
    {
        if (!type.IsGenericType)
        {
            return (type.FullName ?? type.Name).Replace("+", ".");
        }

        var genericDefinition = type.GetGenericTypeDefinition();
        var genericTypeName = (genericDefinition.FullName ?? genericDefinition.Name).Replace("+", ".");
        var backtickIndex = genericTypeName.IndexOf('`');

        if (backtickIndex >= 0)
        {
            genericTypeName = genericTypeName[..backtickIndex];
        }

        var genericArguments = type.GetGenericArguments();
        var genericArgumentsCode = string.Join(", ", genericArguments.Select(GetCodeTypeName));

        return $"{genericTypeName}<{genericArgumentsCode}>";
    }

    internal static string BuildFailureResultCode(
        Type resultType,
        string sourceResultExpression)
    {
        if (resultType == typeof(Result))
        {
            return sourceResultExpression;
        }

        var valueTypeName = GetCodeTypeName(resultType.GetGenericArguments().Single());

        return $"global::PANiXiDA.Core.ResultPattern.Result.Failure<{valueTypeName}>({sourceResultExpression}.Errors)";
    }

    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "middleware";
        }

        if (value.Length == 1)
        {
            return value.ToLowerInvariant();
        }

        return char.ToLowerInvariant(value[0]) + value[1..];
    }
}
