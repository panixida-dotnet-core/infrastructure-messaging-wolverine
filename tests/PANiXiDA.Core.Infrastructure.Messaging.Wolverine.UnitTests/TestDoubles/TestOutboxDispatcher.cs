using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

public sealed class TestOutboxDispatcher : IOutboxDispatcher
{
    public int PublishCallCount { get; private set; }

    public int FlushCallCount { get; private set; }

    public int PersistCallCount { get; private set; }

    public CancellationToken LastPersistCancellationToken { get; private set; }

    public Exception? PersistException { get; set; }

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

    public Task PersistAsync(CancellationToken cancellationToken)
    {
        PersistCallCount++;
        LastPersistCancellationToken = cancellationToken;

        return PersistException is null ? Task.CompletedTask : Task.FromException(PersistException);
    }

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        FlushCallCount++;
        LastFlushCancellationToken = cancellationToken;

        return Task.CompletedTask;
    }
}
