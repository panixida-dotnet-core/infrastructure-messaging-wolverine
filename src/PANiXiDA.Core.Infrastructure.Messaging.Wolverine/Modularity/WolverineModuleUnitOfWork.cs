namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;

internal sealed class WolverineModuleUnitOfWork(
    WolverineModuleExecutionContext moduleContext) : IUnitOfWork
{
    public bool HasActiveTransaction => moduleContext.GetUnitOfWork().HasActiveTransaction;

    public Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        return moduleContext.GetUnitOfWork().BeginTransactionAsync(cancellationToken);
    }

    public Task CommitTransactionAsync(CancellationToken cancellationToken)
    {
        return moduleContext.GetUnitOfWork().CommitTransactionAsync(cancellationToken);
    }

    public Task RollbackTransactionAsync(CancellationToken cancellationToken)
    {
        return moduleContext.GetUnitOfWork().RollbackTransactionAsync(cancellationToken);
    }

    public ValueTask DisposeTransactionAsync()
    {
        return moduleContext.GetUnitOfWork().DisposeTransactionAsync();
    }

    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        return moduleContext
            .GetUnitOfWork()
            .ExecuteInTransactionAsync(action, cancellationToken);
    }
}
