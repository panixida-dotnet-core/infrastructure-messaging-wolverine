using Microsoft.EntityFrameworkCore;

using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

/// <summary>
/// Uses <see cref="IDbContextOutbox{TDbContext}"/> to publish messages through the Wolverine EF Core outbox.
/// </summary>
/// <typeparam name="TDbContext">The DbContext type used by the transactional outbox.</typeparam>
/// <param name="outbox">The Wolverine outbox associated with the current DbContext scope.</param>
public sealed class EfCoreOutboxDispatcher<TDbContext>(IDbContextOutbox<TDbContext> outbox)
    : IOutboxDispatcher
    where TDbContext : DbContext
{
    /// <inheritdoc />
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        return outbox.PublishAsync(@event).AsTask();
    }

    /// <inheritdoc />
    public Task PublishAsync<TEvent>(TEvent @event, DeliveryOptions options, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        return outbox.PublishAsync(@event, options).AsTask();
    }

    /// <inheritdoc />
    public Task SendAsync(ICommand<Result> command, DeliveryOptions options, CancellationToken cancellationToken)
    {
        return outbox.SendAsync(command, options).AsTask();
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return outbox.DbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task FlushAsync(CancellationToken cancellationToken)
    {
        return outbox.FlushOutgoingMessagesAsync();
    }
}
