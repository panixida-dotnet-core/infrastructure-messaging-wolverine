namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

public sealed class TestUnitOfWork : IUnitOfWork
{
    public bool HasActiveTransaction { get; set; }

    public int BeginTransactionCallCount { get; private set; }

    public int CommitTransactionCallCount { get; private set; }

    public int RollbackTransactionCallCount { get; private set; }

    public int DisposeTransactionCallCount { get; private set; }

    public Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        BeginTransactionCallCount++;
        HasActiveTransaction = true;

        return Task.CompletedTask;
    }

    public Task CommitTransactionAsync(CancellationToken cancellationToken)
    {
        CommitTransactionCallCount++;
        HasActiveTransaction = false;

        return Task.CompletedTask;
    }

    public Task RollbackTransactionAsync(CancellationToken cancellationToken)
    {
        RollbackTransactionCallCount++;
        HasActiveTransaction = false;

        return Task.CompletedTask;
    }

    public ValueTask DisposeTransactionAsync()
    {
        DisposeTransactionCallCount++;
        HasActiveTransaction = false;

        return ValueTask.CompletedTask;
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        await BeginTransactionAsync(cancellationToken);

        try
        {
            await action(cancellationToken);
            await CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
