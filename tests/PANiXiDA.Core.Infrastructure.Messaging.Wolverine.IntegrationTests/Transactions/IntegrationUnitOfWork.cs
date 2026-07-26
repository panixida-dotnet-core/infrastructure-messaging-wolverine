using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Transactions;

public sealed class IntegrationUnitOfWork<TDbContext>(
    TDbContext dbContext,
    IntegrationTestJournal journal) : IUnitOfWork
    where TDbContext : DbContext
{
    private IDbContextTransaction? transaction;

    public bool HasActiveTransaction => transaction is not null;

    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            return;
        }

        journal.Add("unitOfWork.begin");
        journal.Add($"unitOfWork.begin:{typeof(TDbContext).Name}");
        transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken)
    {
        if (transaction is null)
        {
            return;
        }

        journal.Add("unitOfWork.commit");
        journal.Add($"unitOfWork.commit:{typeof(TDbContext).Name}");

        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
        transaction = null;
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken)
    {
        journal.Add("unitOfWork.rollback");
        journal.Add($"unitOfWork.rollback:{typeof(TDbContext).Name}");

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
            await CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
