using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

public sealed class TestOutboxDispatcher : IOutboxDispatcher
{
    public int PublishCallCount { get; private set; }

    public int FlushCallCount { get; private set; }

    public int SaveChangesCallCount { get; private set; }

    public CancellationToken LastSaveChangesCancellationToken { get; private set; }

    public Exception? SaveChangesException { get; set; }

    public object? LastPublishedEvent { get; private set; }

    public CancellationToken LastFlushCancellationToken { get; private set; }

    public int SendCallCount { get; private set; }

    public object? LastSentMessage { get; private set; }

    public DeliveryOptions? LastDeliveryOptions { get; private set; }

    public CancellationToken LastDispatchCancellationToken { get; private set; }

    public Exception? DispatchException { get; set; }

    public Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        PublishCallCount++;
        LastPublishedEvent = @event;

        return Task.CompletedTask;
    }

    public Task PublishAsync<TEvent>(TEvent @event, DeliveryOptions options, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        PublishCallCount++;
        LastPublishedEvent = @event;
        LastDeliveryOptions = options;
        LastDispatchCancellationToken = cancellationToken;

        return DispatchException is null ? Task.CompletedTask : Task.FromException(DispatchException);
    }

    public Task SendAsync(ICommand<Result> command, DeliveryOptions options, CancellationToken cancellationToken)
    {
        SendCallCount++;
        LastSentMessage = command;
        LastDeliveryOptions = options;
        LastDispatchCancellationToken = cancellationToken;

        return DispatchException is null ? Task.CompletedTask : Task.FromException(DispatchException);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCallCount++;
        LastSaveChangesCancellationToken = cancellationToken;

        return SaveChangesException is null ? Task.CompletedTask : Task.FromException(SaveChangesException);
    }

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        FlushCallCount++;
        LastFlushCancellationToken = cancellationToken;

        return Task.CompletedTask;
    }
}
