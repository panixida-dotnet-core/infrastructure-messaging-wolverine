namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

internal sealed class RequestMiddlewareRegistry(
    IReadOnlyList<Type> beforeMiddlewareTypes,
    IReadOnlyList<Type> afterMiddlewareTypes,
    IReadOnlyList<Type> finallyMiddlewareTypes)
{
    internal IReadOnlyList<Type> BeforeMiddlewareTypes { get; } = beforeMiddlewareTypes;
    internal IReadOnlyList<Type> AfterMiddlewareTypes { get; } = afterMiddlewareTypes;
    internal IReadOnlyList<Type> FinallyMiddlewareTypes { get; } = finallyMiddlewareTypes;

    internal static RequestMiddlewareRegistry Empty { get; } = new(
        [],
        [],
        []);

    internal static RequestMiddlewareRegistry Create(Action<RequestMiddlewareRegistryBuilder> configure)
    {
        var builder = RequestMiddlewareRegistryBuilder.Create();
        configure(builder);

        return builder.Build();
    }
}
