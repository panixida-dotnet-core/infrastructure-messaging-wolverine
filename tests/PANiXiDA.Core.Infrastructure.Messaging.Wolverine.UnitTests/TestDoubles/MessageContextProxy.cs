using System.Reflection;

using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

public class MessageContextProxy : DispatchProxy
{
    public object? LastPublishedMessage { get; private set; }

    public int PublishCallCount { get; private set; }

    public static IMessageContext Create(out MessageContextProxy proxy)
    {
        var messageContext =
            DispatchProxy.Create<IMessageContext, MessageContextProxy>();
        proxy = (MessageContextProxy)(object)messageContext!;

        return messageContext!;
    }

    protected override object? Invoke(
        MethodInfo? targetMethod,
        object?[]? args)
    {
        if (targetMethod?.Name == nameof(IMessageBus.PublishAsync))
        {
            PublishCallCount++;
            LastPublishedMessage = args?[0];

            return ValueTask.CompletedTask;
        }

        throw new NotSupportedException(
            $"Method '{targetMethod?.Name}' is not supported by the test proxy.");
    }
}
