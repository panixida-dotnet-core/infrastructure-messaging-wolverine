using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine;

internal sealed class WolverineMediator(
    IMessageBus messageBus) : IMediator
{
    public Task<TResult> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken)
        where TResult : Result
    {
        return messageBus.InvokeAsync<TResult>(command, cancellationToken);
    }

    public Task<TResult> QueryAsync<TResult>(
        IQuery<TResult> query,
        CancellationToken cancellationToken)
        where TResult : Result
    {
        return messageBus.InvokeAsync<TResult>(query, cancellationToken);
    }
}
