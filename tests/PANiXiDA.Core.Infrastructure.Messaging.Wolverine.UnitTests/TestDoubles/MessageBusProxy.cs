using System.Reflection;

using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

public class MessageBusProxy : DispatchProxy
{
    public object? LastMessage { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public Result Result { get; set; } = Result.Success();

    public object? GenericResult { get; set; }

    public static IMessageBus Create(out MessageBusProxy proxy)
    {
        var messageBus = DispatchProxy.Create<IMessageBus, MessageBusProxy>();
        proxy = (MessageBusProxy)(object)messageBus!;

        return messageBus!;
    }

    protected override object? Invoke(
        MethodInfo? targetMethod,
        object?[]? args)
    {
        if (targetMethod?.Name == nameof(IMessageBus.InvokeAsync) &&
            targetMethod.IsGenericMethod &&
            args is { Length: >= 2 })
        {
            LastMessage = args[0];
            LastCancellationToken = (CancellationToken)args[1]!;

            var resultType = targetMethod.GetGenericArguments()[0];
            var result = resultType == typeof(Result)
                ? Result
                : GenericResult;

            return typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [result]);
        }

        throw new NotSupportedException($"Method '{targetMethod?.Name}' is not supported by the test proxy.");
    }
}
