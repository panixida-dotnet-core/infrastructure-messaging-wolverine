using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;

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
        if (RequestBehaviorMetadata.GetBehavior(behaviorType).PublicConstructorCount != 1)
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
        if (!RequestBehaviorMetadata.GetBehavior(behaviorType).Contracts.Contains(expectedBehaviorInterfaceType))
        {
            throw new InvalidOperationException(
                $"{stageName} middleware '{behaviorType.FullName}' must implement '{expectedBehaviorInterfaceType.FullName}'.");
        }
    }
}
