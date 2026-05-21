using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;

/// <summary>
/// Configures request behaviors for one pipeline stage.
/// </summary>
public sealed class WolverineRequestBehaviorStageConfiguration
{
    private readonly List<Type> behaviorTypes = [];
    private readonly Type expectedBehaviorInterfaceType;
    private readonly string stageName;

    internal WolverineRequestBehaviorStageConfiguration(
        Type expectedBehaviorInterfaceType,
        string stageName)
    {
        this.expectedBehaviorInterfaceType = expectedBehaviorInterfaceType;
        this.stageName = stageName;
    }

    /// <summary>
    /// Appends a behavior to the current stage.
    /// </summary>
    /// <param name="behaviorType">The behavior type to append.</param>
    /// <returns>The same configuration instance for fluent configuration.</returns>
    public WolverineRequestBehaviorStageConfiguration Add(Type behaviorType)
    {
        ValidateBehaviorType(behaviorType);
        behaviorTypes.Add(behaviorType);

        return this;
    }

    /// <summary>
    /// Appends a behavior to the current stage.
    /// </summary>
    /// <typeparam name="TBehavior">The behavior type to append.</typeparam>
    /// <returns>The same configuration instance for fluent configuration.</returns>
    public WolverineRequestBehaviorStageConfiguration Add<TBehavior>()
    {
        return Add(typeof(TBehavior));
    }

    /// <summary>
    /// Inserts a behavior before another behavior in the current stage.
    /// </summary>
    /// <typeparam name="TBehavior">The behavior type to insert.</typeparam>
    /// <typeparam name="TAnchorBehavior">The existing behavior type used as an insertion anchor.</typeparam>
    /// <returns>The same configuration instance for fluent configuration.</returns>
    public WolverineRequestBehaviorStageConfiguration InsertBefore<TBehavior, TAnchorBehavior>()
    {
        return InsertBefore(
            typeof(TBehavior),
            typeof(TAnchorBehavior));
    }

    /// <summary>
    /// Inserts a behavior before another behavior in the current stage.
    /// </summary>
    /// <param name="behaviorType">The behavior type to insert.</param>
    /// <param name="anchorBehaviorType">The existing behavior type used as an insertion anchor.</param>
    /// <returns>The same configuration instance for fluent configuration.</returns>
    public WolverineRequestBehaviorStageConfiguration InsertBefore(
        Type behaviorType,
        Type anchorBehaviorType)
    {
        ValidateBehaviorType(behaviorType);
        ValidateBehaviorType(anchorBehaviorType);

        var anchorIndex = FindAnchorIndex(anchorBehaviorType);
        behaviorTypes.Insert(anchorIndex, behaviorType);

        return this;
    }

    /// <summary>
    /// Inserts a behavior after another behavior in the current stage.
    /// </summary>
    /// <typeparam name="TBehavior">The behavior type to insert.</typeparam>
    /// <typeparam name="TAnchorBehavior">The existing behavior type used as an insertion anchor.</typeparam>
    /// <returns>The same configuration instance for fluent configuration.</returns>
    public WolverineRequestBehaviorStageConfiguration InsertAfter<TBehavior, TAnchorBehavior>()
    {
        return InsertAfter(
            typeof(TBehavior),
            typeof(TAnchorBehavior));
    }

    /// <summary>
    /// Inserts a behavior after another behavior in the current stage.
    /// </summary>
    /// <param name="behaviorType">The behavior type to insert.</param>
    /// <param name="anchorBehaviorType">The existing behavior type used as an insertion anchor.</param>
    /// <returns>The same configuration instance for fluent configuration.</returns>
    public WolverineRequestBehaviorStageConfiguration InsertAfter(
        Type behaviorType,
        Type anchorBehaviorType)
    {
        ValidateBehaviorType(behaviorType);
        ValidateBehaviorType(anchorBehaviorType);

        var anchorIndex = FindAnchorIndex(anchorBehaviorType);
        behaviorTypes.Insert(anchorIndex + 1, behaviorType);

        return this;
    }

    internal IReadOnlyList<Type> Build()
    {
        return [.. behaviorTypes];
    }

    private void ValidateBehaviorType(Type behaviorType)
    {
        RequestMiddlewareRegistrationValidator.ValidateBehaviorRegistration(
            behaviorType,
            expectedBehaviorInterfaceType,
            stageName);
    }

    private int FindAnchorIndex(Type anchorBehaviorType)
    {
        var anchorIndex = behaviorTypes.IndexOf(anchorBehaviorType);
        if (anchorIndex >= 0)
        {
            return anchorIndex;
        }

        throw new InvalidOperationException(
            $"{stageName} behavior '{anchorBehaviorType.FullName}' was not registered.");
    }
}
