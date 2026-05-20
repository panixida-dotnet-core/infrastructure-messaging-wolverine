using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Behaviors;

/// <summary>
/// Flushes Wolverine outbox messages after a successful command request.
/// </summary>
/// <typeparam name="TCommand">The command type processed by the request pipeline.</typeparam>
/// <typeparam name="TResult">The command result type.</typeparam>
/// <param name="unitOfWork">The unit of work used to inspect the current transaction state.</param>
/// <param name="outboxDispatcher">The dispatcher used to flush accumulated outgoing messages.</param>
public sealed class FlushOutgoingMessagesBehavior<TCommand, TResult>(
    IUnitOfWork unitOfWork,
    IOutboxDispatcher outboxDispatcher)
    : IAfterRequestBehavior<TCommand, TResult>
    where TCommand : ICommand<TResult>
    where TResult : Result
{
    /// <summary>
    /// Flushes outgoing messages when the command result is successful and no transaction is active.
    /// </summary>
    /// <param name="request">The processed command.</param>
    /// <param name="result">The command result.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task AfterAsync(
        TCommand request,
        TResult result,
        CancellationToken cancellationToken)
    {
        if (!result.IsSuccess)
        {
            return Task.CompletedTask;
        }

        if (unitOfWork.HasActiveTransaction)
        {
            return Task.CompletedTask;
        }

        return outboxDispatcher.FlushAsync(cancellationToken);
    }
}
