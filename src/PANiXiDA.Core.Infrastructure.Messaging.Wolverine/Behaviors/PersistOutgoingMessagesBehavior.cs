using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Behaviors;

/// <summary>
/// Persists tracked outbox messages before a successful command transaction is committed.
/// </summary>
/// <typeparam name="TCommand">The command type processed by the request pipeline.</typeparam>
/// <typeparam name="TResult">The command result type.</typeparam>
/// <param name="unitOfWork">The unit of work that owns the current transaction.</param>
/// <param name="outboxDispatcher">The outbox associated with that transaction.</param>
public sealed class PersistOutgoingMessagesBehavior<TCommand, TResult>(
    IUnitOfWork unitOfWork,
    IOutboxDispatcher outboxDispatcher) : IAfterRequestBehavior<TCommand, TResult>
    where TCommand : ICommand<TResult>
    where TResult : Result
{
    /// <inheritdoc />
    public Task AfterAsync(TCommand request, TResult result, CancellationToken cancellationToken)
    {
        return result.IsSuccess && unitOfWork.HasActiveTransaction
            ? outboxDispatcher.PersistAsync(cancellationToken)
            : Task.CompletedTask;
    }
}
