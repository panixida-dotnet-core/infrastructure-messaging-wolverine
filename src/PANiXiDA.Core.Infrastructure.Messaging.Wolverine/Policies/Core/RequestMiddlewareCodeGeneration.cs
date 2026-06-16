using JasperFx.CodeGeneration.Model;

using System.Reflection;

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
        if (!middlewareType.IsGenericTypeDefinition)
        {
            if (middlewareType.ContainsGenericParameters)
            {
                closedMiddlewareType = null!;
                return false;
            }

            if (!SupportsRequest(
                    middlewareType,
                    requestType,
                    resultType,
                    behaviorInterfaceType))
            {
                closedMiddlewareType = null!;
                return false;
            }

            closedMiddlewareType = middlewareType;
            return true;
        }

        var genericArguments = middlewareType.GetGenericArguments();
        if (genericArguments.Length != 2)
        {
            throw new InvalidOperationException(
                $"Open generic middleware '{middlewareType.FullName}' must have exactly 2 generic parameters.");
        }

        try
        {
            var candidate = middlewareType.MakeGenericType(requestType, resultType);

            if (!SupportsRequest(
                    candidate,
                    requestType,
                    resultType,
                    behaviorInterfaceType))
            {
                closedMiddlewareType = null!;
                return false;
            }

            closedMiddlewareType = candidate;
            return true;
        }
        catch (ArgumentException)
        {
            closedMiddlewareType = null!;
            return false;
        }
    }

    internal static ConstructorInfo ResolveConstructor(Type middlewareType)
    {
        var constructors = middlewareType.GetConstructors(BindingFlags.Instance | BindingFlags.Public);

        if (constructors.Length != 1)
        {
            throw new InvalidOperationException(
                $"Type '{middlewareType.FullName}' must have exactly one public constructor.");
        }

        return constructors[0];
    }

    internal static Variable[] ResolveConstructorVariables(
        IMethodVariables chain,
        ConstructorInfo constructor)
    {
        var parameters = constructor.GetParameters();
        var variables = new Variable[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            variables[i] = chain.FindVariable(parameters[i].ParameterType);
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

        if (resultType.IsGenericType &&
            resultType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var valueTypeName = GetCodeTypeName(resultType.GetGenericArguments()[0]);

            return $"global::PANiXiDA.Core.ResultPattern.Result.Failure<{valueTypeName}>({sourceResultExpression}.Errors)";
        }

        throw new InvalidOperationException(
            $"Result type '{resultType.FullName}' must be Result or Result<T>.");
    }

    private static bool SupportsRequest(
        Type closedMiddlewareType,
        Type requestType,
        Type resultType,
        Type behaviorInterfaceType)
    {
        var behaviorInterfaces = closedMiddlewareType
            .GetInterfaces()
            .Where(item =>
                item.IsGenericType &&
                item.GetGenericTypeDefinition() == behaviorInterfaceType);

        foreach (var behaviorInterface in behaviorInterfaces)
        {
            var genericArguments = behaviorInterface.GetGenericArguments();
            var declaredRequestType = genericArguments[0];
            var declaredResultType = genericArguments[1];

            if (declaredRequestType.IsAssignableFrom(requestType) &&
                declaredResultType.IsAssignableFrom(resultType))
            {
                return true;
            }
        }

        return false;
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
