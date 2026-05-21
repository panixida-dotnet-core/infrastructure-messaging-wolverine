using Microsoft.EntityFrameworkCore.Storage;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Transactions;

public sealed class IntegrationUnitOfWork(
    IntegrationDbContext dbContext,
    IntegrationTestJournal journal) : IUnitOfWork
{
    private IDbContextTransaction? transaction;

    public bool HasActiveTransaction => transaction is not null;

    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        journal.Add("unitOfWork.begin");
        transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        journal.Add("unitOfWork.save");
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken)
    {
        journal.Add("unitOfWork.commit");

        if (transaction is null)
        {
            return;
        }

        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
        transaction = null;
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken)
    {
        journal.Add("unitOfWork.rollback");

        if (transaction is null)
        {
            return;
        }

        await transaction.RollbackAsync(cancellationToken);
        await transaction.DisposeAsync();
        transaction = null;
    }

    public async ValueTask DisposeTransactionAsync()
    {
        if (transaction is not null)
        {
            await transaction.DisposeAsync();
            transaction = null;
        }
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        await BeginTransactionAsync(cancellationToken);

        try
        {
            await action(cancellationToken);
            await SaveChangesAsync(cancellationToken);
            await CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
