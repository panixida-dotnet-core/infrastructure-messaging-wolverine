using System.Reflection;

using Microsoft.EntityFrameworkCore;

using Wolverine.EntityFrameworkCore;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

public class DbContextOutboxProxy<TDbContext> : DispatchProxy
    where TDbContext : DbContext
{
    public object? LastPublishedMessage { get; private set; }

    public int PublishCallCount { get; private set; }

    public int FlushCallCount { get; private set; }

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

            return ValueTask.CompletedTask;
        }

        if (targetMethod?.Name == nameof(IDbContextOutbox<>.FlushOutgoingMessagesAsync))
        {
            FlushCallCount++;

            return Task.CompletedTask;
        }

        throw new NotSupportedException($"Method '{targetMethod?.Name}' is not supported by the test proxy.");
    }
}
