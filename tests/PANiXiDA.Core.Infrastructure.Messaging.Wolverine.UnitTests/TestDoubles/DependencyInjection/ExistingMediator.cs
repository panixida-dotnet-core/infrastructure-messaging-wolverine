namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.DependencyInjection;

public sealed class ExistingMediator : IMediator
{
    public Task<TResult> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken)
        where TResult : Result
    {
        throw new NotSupportedException();
    }

    public Task<TResult> QueryAsync<TResult>(
        IQuery<TResult> query,
        CancellationToken cancellationToken)
        where TResult : Result
    {
        throw new NotSupportedException();
    }
}
