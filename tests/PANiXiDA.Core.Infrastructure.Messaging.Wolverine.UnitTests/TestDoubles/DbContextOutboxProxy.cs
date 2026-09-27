using System.Reflection;

using Microsoft.EntityFrameworkCore;

using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

public class DbContextOutboxProxy<TDbContext> : DispatchProxy
    where TDbContext : DbContext
{
    public object? LastPublishedMessage { get; private set; }

    public int PublishCallCount { get; private set; }

    public int FlushCallCount { get; private set; }

    public object? LastSentMessage { get; private set; }

    public int SendCallCount { get; private set; }

    public DeliveryOptions? LastDeliveryOptions { get; private set; }

    public Exception? DispatchException { get; set; }

    public static IDbContextOutbox<TDbContext> Create(out DbContextOutboxProxy<TDbContext> proxy)
    {
        var outbox = DispatchProxy.Create<IDbContextOutbox<TDbContext>, DbContextOutboxProxy<TDbContext>>();
        proxy = (DbContextOutboxProxy<TDbContext>)(object)outbox!;

        return outbox!;
    }

    protected override object? Invoke(
        MethodInfo? targetMethod,
        object?[]? args)
    {
        if (targetMethod?.Name == nameof(IDbContextOutbox<>.PublishAsync))
        {
            PublishCallCount++;
            LastPublishedMessage = args?[0];
            LastDeliveryOptions = args?[1] as DeliveryOptions;

            return DispatchException is null ? ValueTask.CompletedTask : new ValueTask(Task.FromException(DispatchException));
        }

        if (targetMethod?.Name == nameof(IMessageBus.SendAsync))
        {
            SendCallCount++;
            LastSentMessage = args?[0];
            LastDeliveryOptions = args?[1] as DeliveryOptions;

            return DispatchException is null ? ValueTask.CompletedTask : new ValueTask(Task.FromException(DispatchException));
        }

        if (targetMethod?.Name == nameof(IDbContextOutbox<>.FlushOutgoingMessagesAsync))
        {
            FlushCallCount++;

            return Task.CompletedTask;
        }

        throw new NotSupportedException($"Method '{targetMethod?.Name}' is not supported by the test proxy.");
    }
}
