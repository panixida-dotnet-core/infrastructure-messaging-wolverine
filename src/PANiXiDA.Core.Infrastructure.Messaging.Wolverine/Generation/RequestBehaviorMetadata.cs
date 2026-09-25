using System.Collections.Concurrent;
using System.ComponentModel;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;

/// <summary>
/// Stores compile-time request behavior metadata emitted by the package's source generator.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class RequestBehaviorMetadata
{
    private static readonly ConcurrentDictionary<Type, Behavior> behaviors = new();
    private static readonly ConcurrentDictionary<(Type Behavior, Type Request, Type Result, Type Contract), Type> bindings = new();

    /// <summary>
    /// Registers the constructor and supported contracts of a behavior known at compile time.
    /// </summary>
    public static void RegisterBehavior(Type behaviorType, int publicConstructorCount, Type[] contracts, Type[] constructorParameters)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);
        ArgumentOutOfRangeException.ThrowIfNegative(publicConstructorCount);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(constructorParameters);
        behaviors.TryAdd(behaviorType, new Behavior(publicConstructorCount, [.. contracts], [.. constructorParameters]));
    }

    /// <summary>
    /// Registers a statically closed behavior for a request, result, and pipeline stage, or null for an unsupported combination.
    /// </summary>
    public static void RegisterBinding(Type behaviorType, Type requestType, Type resultType, Type contractType, Type? closedBehaviorType)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(resultType);
        ArgumentNullException.ThrowIfNull(contractType);
        bindings.TryAdd((behaviorType, requestType, resultType, contractType), closedBehaviorType ?? typeof(void));
    }

    internal static Behavior GetBehavior(Type type)
    {
        return behaviors.TryGetValue(type, out var behavior)
            ? behavior
            : throw new InvalidOperationException(
                $"No generated request behavior metadata exists for '{type.FullName}'. Ensure the Wolverine package source generator is enabled in the project declaring or registering the behavior.");
    }

    internal static bool TryResolve(Type behavior, Type request, Type result, Type contract, out Type closedBehavior)
    {
        closedBehavior = null!;
        if (!GetBehavior(behavior).Contracts.Contains(contract) || !typeof(IRequest<Result>).IsAssignableFrom(request))
        {
            return false;
        }

        if (!bindings.TryGetValue((behavior, request, result, contract), out var resolved))
        {
            throw new InvalidOperationException(
                $"No generated request behavior binding exists for '{behavior.FullName}', '{request.FullName}', and '{result.FullName}'. Ensure the Wolverine package source generator can see the behavior and request types in the host project.");
        }

        if (resolved == typeof(void))
        {
            return false;
        }

        closedBehavior = resolved;
        return true;
    }

    internal sealed record Behavior(int PublicConstructorCount, IReadOnlyList<Type> Contracts, IReadOnlyList<Type> ConstructorParameters);
}
