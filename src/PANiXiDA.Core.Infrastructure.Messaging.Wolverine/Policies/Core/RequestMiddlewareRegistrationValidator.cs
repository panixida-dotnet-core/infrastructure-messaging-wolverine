using System.Reflection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

internal static class RequestMiddlewareRegistrationValidator
{
    internal static void ValidateBehaviorRegistration(
        Type behaviorType,
        Type expectedBehaviorInterfaceType,
        string stageName)
    {
        if (!expectedBehaviorInterfaceType.IsInterface || !expectedBehaviorInterfaceType.IsGenericTypeDefinition)
        {
            throw new InvalidOperationException(
                $"Expected behavior interface '{expectedBehaviorInterfaceType.FullName}' must be an open generic interface.");
        }

        ValidateIsConcreteOrOpenGeneric(behaviorType, stageName);
        ValidateConstructor(behaviorType);
        ValidateImplementsExpectedBehaviorInterface(behaviorType, expectedBehaviorInterfaceType, stageName);
    }

    private static void ValidateIsConcreteOrOpenGeneric(Type behaviorType, string stageName)
    {
        if (behaviorType.IsInterface)
        {
            throw new InvalidOperationException(
                $"{stageName} middleware '{behaviorType.FullName}' must be a class, not an interface.");
        }

        if (behaviorType.IsAbstract)
        {
            throw new InvalidOperationException(
                $"{stageName} middleware '{behaviorType.FullName}' must not be abstract.");
        }

        if (behaviorType.ContainsGenericParameters && !behaviorType.IsGenericTypeDefinition)
        {
            throw new InvalidOperationException(
                $"{stageName} middleware '{behaviorType.FullName}' must be either a closed type or an open generic type definition.");
        }

        if (behaviorType.IsGenericTypeDefinition)
        {
            var genericArguments = behaviorType.GetGenericArguments();
            if (genericArguments.Length != 2)
            {
                throw new InvalidOperationException(
                    $"{stageName} middleware '{behaviorType.FullName}' must have exactly 2 generic parameters.");
            }
        }
    }

    private static void ValidateConstructor(Type behaviorType)
    {
        var constructors = behaviorType.GetConstructors(BindingFlags.Instance | BindingFlags.Public);

        if (constructors.Length != 1)
        {
            throw new InvalidOperationException(
                $"Middleware '{behaviorType.FullName}' must have exactly one public constructor.");
        }
    }

    private static void ValidateImplementsExpectedBehaviorInterface(
        Type behaviorType,
        Type expectedBehaviorInterfaceType,
        string stageName)
    {
        if (behaviorType.IsGenericTypeDefinition)
        {
            var implementsExpectedOpenGenericInterface = behaviorType
                .GetInterfaces()
                .Any(item =>
                    item.IsGenericType &&
                    item.GetGenericTypeDefinition() == expectedBehaviorInterfaceType);

            if (!implementsExpectedOpenGenericInterface)
            {
                throw new InvalidOperationException(
                    $"{stageName} middleware '{behaviorType.FullName}' must implement '{expectedBehaviorInterfaceType.FullName}'.");
            }

            return;
        }

        var implementsExpectedClosedInterface = behaviorType
            .GetInterfaces()
            .Any(item =>
                item.IsGenericType &&
                item.GetGenericTypeDefinition() == expectedBehaviorInterfaceType);

        if (!implementsExpectedClosedInterface)
        {
            throw new InvalidOperationException(
                $"{stageName} middleware '{behaviorType.FullName}' must implement '{expectedBehaviorInterfaceType.FullName}'.");
        }
    }
}
