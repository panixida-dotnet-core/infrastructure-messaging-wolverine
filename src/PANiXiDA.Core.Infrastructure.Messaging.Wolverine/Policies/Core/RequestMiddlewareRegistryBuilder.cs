namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

internal sealed class RequestMiddlewareRegistryBuilder
{
    private readonly List<Type> beforeMiddlewareTypes = [];
    private readonly List<Type> afterMiddlewareTypes = [];
    private readonly List<Type> finallyMiddlewareTypes = [];

    internal static RequestMiddlewareRegistryBuilder Create()
    {
        return new RequestMiddlewareRegistryBuilder();
    }

    internal RequestMiddlewareRegistryBuilder AddBefore<TBehavior>()
    {
        return AddBefore(typeof(TBehavior));
    }

    internal RequestMiddlewareRegistryBuilder AddBefore(Type behaviorType)
    {
        RequestMiddlewareRegistrationValidator.ValidateBehaviorRegistration(
            behaviorType,
            typeof(IBeforeRequestBehavior<,>),
            "Before");

        beforeMiddlewareTypes.Add(behaviorType);

        return this;
    }

    internal RequestMiddlewareRegistryBuilder AddBefore(params Type[] behaviorTypes)
    {
        foreach (var behaviorType in behaviorTypes)
        {
            AddBefore(behaviorType);
        }

        return this;
    }

    internal RequestMiddlewareRegistryBuilder AddAfter<TBehavior>()
    {
        return AddAfter(typeof(TBehavior));
    }

    internal RequestMiddlewareRegistryBuilder AddAfter(Type behaviorType)
    {
        RequestMiddlewareRegistrationValidator.ValidateBehaviorRegistration(
            behaviorType,
            typeof(IAfterRequestBehavior<,>),
            "After");

        afterMiddlewareTypes.Add(behaviorType);

        return this;
    }

    internal RequestMiddlewareRegistryBuilder AddAfter(params Type[] behaviorTypes)
    {
        foreach (var behaviorType in behaviorTypes)
        {
            AddAfter(behaviorType);
        }

        return this;
    }

    internal RequestMiddlewareRegistryBuilder AddFinally<TBehavior>()
    {
        return AddFinally(typeof(TBehavior));
    }

    internal RequestMiddlewareRegistryBuilder AddFinally(Type behaviorType)
    {
        RequestMiddlewareRegistrationValidator.ValidateBehaviorRegistration(
            behaviorType,
            typeof(IFinallyRequestBehavior<,>),
            "Finally");

        finallyMiddlewareTypes.Add(behaviorType);

        return this;
    }

    internal RequestMiddlewareRegistryBuilder AddFinally(params Type[] behaviorTypes)
    {
        foreach (var behaviorType in behaviorTypes)
        {
            AddFinally(behaviorType);
        }

        return this;
    }

    internal RequestMiddlewareRegistry Build()
    {
        return new RequestMiddlewareRegistry(
            [.. beforeMiddlewareTypes],
            [.. afterMiddlewareTypes],
            [.. finallyMiddlewareTypes]);
    }
}
