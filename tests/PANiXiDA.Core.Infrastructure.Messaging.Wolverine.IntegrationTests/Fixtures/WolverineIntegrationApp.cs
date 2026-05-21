using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Fixtures;

public sealed class WolverineIntegrationApp(
    IHost host,
    IntegrationTestJournal journal,
    string connectionString) : IAsyncDisposable
{
    public IHost Host => host;

    public IntegrationTestJournal Journal => journal;

    public async ValueTask DisposeAsync()
    {
        await host.StopAsync();
        host.Dispose();
    }

    public async Task<TResult> ExecuteWithMediatorAsync<TResult>(
        Func<IMediator, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        using var scope = host.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        return await action(mediator, cancellationToken);
    }

    public async Task<int> CountRecordsAsync(Guid id)
    {
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();

        return await dbContext.Records.CountAsync(x => x.Id == id);
    }

    public async Task<int> CountHandledEventsAsync(Guid eventId)
    {
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();

        return await dbContext.HandledEvents.CountAsync(x => x.EventId == eventId);
    }

    public async Task<long> CountRowsAsync(string tableName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"select count(*) from {tableName}";

        var result = await command.ExecuteScalarAsync();

        return (long)result!;
    }

    public static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout)
    {
        var startedAt = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow - startedAt < timeout)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException("The expected integration test condition was not reached in time.");
    }
}
