using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine;

/// <summary>
/// Wolverine-backed implementation of the PANiXiDA mediator abstraction.
/// </summary>
/// <param name="messageBus">The Wolverine message bus used to invoke request handlers.</param>
public sealed class WolverineMediator(
    IMessageBus messageBus) : IMediator
{
    /// <inheritdoc />
    public Task<TResult> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken)
        where TResult : Result
    {
        return messageBus.InvokeAsync<TResult>(command, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TResult> QueryAsync<TResult>(
        IQuery<TResult> query,
        CancellationToken cancellationToken)
        where TResult : Result
    {
        return messageBus.InvokeAsync<TResult>(query, cancellationToken);
    }
}
