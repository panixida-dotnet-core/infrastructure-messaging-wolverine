using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Fixtures;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Telemetry;

using Wolverine;
using Wolverine.RDBMS;
using Wolverine.Runtime;
using Wolverine.Persistence.Durability;


namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests;

public sealed class PersistenceMetricsIntegrationTests(PostgreSqlContainerFixture fixture)
    : IClassFixture<PostgreSqlContainerFixture>
{
    [Theory(DisplayName = "PostgreSQL metrics report DLQ removals and exclude future and handled messages from overdue work")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReaderShouldReportPersistedQueueState(bool useModules)
    {
        var token = TestContext.Current.CancellationToken;
        await using var app = await fixture.CreateApplicationAsync(useModuleRouting: useModules,
            configureWolverine: options => options.Durability.Mode = DurabilityMode.MediatorOnly);
        app.Host.Services.GetServices<IHostedService>().OfType<PersistenceMetricsService>().ShouldHaveSingleItem();
        var reader = app.Host.Services.GetRequiredService<IPersistenceMetricsReader>();
        var store = (MessageDatabase<NpgsqlConnection>)app.Host.Services.GetRequiredService<IWolverineRuntime>().Storage;
        using var identifiers = new NpgsqlCommandBuilder();
        var schema = identifiers.QuoteIdentifier(store.SchemaName);
        var empty = await reader.ReadAsync(token);
        empty.DeadLetters.ShouldBe(0);
        empty.OldestDeadLetterAgeSeconds.ShouldBe(0);
        empty.ScheduledDue.ShouldBe(0);
        empty.Schedules.ShouldBeEmpty();

        await using var insert = store.CreateCommand($"""
            insert into {schema}.wolverine_dead_letters (id, body, message_type, received_at, sent_at, replayable)
            values (gen_random_uuid(), '\x00', 'test', 'local://test', now() - interval '2 hours', false),
                   (gen_random_uuid(), '\x00', 'test', 'local://test', now() - interval '1 hour', true);
            insert into {schema}.wolverine_incoming_envelopes (id, status, owner_id, body, message_type, received_at, execution_time)
            values (gen_random_uuid(), 'Scheduled', 0, '\x00', 'test', 'local://test', now() - interval '10 minutes'),
                   (gen_random_uuid(), 'Scheduled', 0, '\x00', 'test', 'local://test', now() + interval '1 day'),
                   (gen_random_uuid(), 'Handled', 0, '\x00', 'test', 'local://test', now() - interval '1 day');
            """);
        await insert.ExecuteNonQueryAsync(token);

        var populated = await reader.ReadAsync(token);
        populated.DeadLetters.ShouldBe(2);
        populated.OldestDeadLetterAgeSeconds.ShouldNotBeNull().ShouldBeInRange(7200, 7210);
        populated.ScheduledDue.ShouldBe(1);
        populated.ScheduledOverdueSeconds.ShouldNotBeNull().ShouldBeInRange(600, 610);

        await using var unknownDate = store.CreateCommand($"update {schema}.wolverine_dead_letters set sent_at = null;");
        await unknownDate.ExecuteNonQueryAsync(token);
        (await reader.ReadAsync(token)).OldestDeadLetterAgeSeconds.ShouldBeNull();

        await using var delete = store.CreateCommand($"delete from {schema}.wolverine_dead_letters; delete from {schema}.wolverine_incoming_envelopes;");
        await delete.ExecuteNonQueryAsync(token);
        var cleared = await reader.ReadAsync(token);
        cleared.DeadLetters.ShouldBe(0);
        cleared.OldestDeadLetterAgeSeconds.ShouldBe(0);
        cleared.ScheduledDue.ShouldBe(0);
        cleared.ScheduledOverdueSeconds.ShouldBe(0);
    }

    [Fact(DisplayName = "Recurring metrics use durable state and exclude removed and paused schedules from overdue age")]
    public async Task ReaderShouldReportRegisteredRecurringSchedules()
    {
        var token = TestContext.Current.CancellationToken;
        await using var app = await fixture.CreateApplicationAsync(configureWolverine: options =>
        {
            options.Durability.Mode = DurabilityMode.MediatorOnly;
            options.Durability.EnableRecurringMessages = true;
        });
        var store = app.Host.Services.GetRequiredService<IWolverineRuntime>().Storage;
        var now = DateTimeOffset.UtcNow;
        foreach (var name in new[] { "active", "paused", "pending", "future", "removed" })
        {
            await store.RecurringMessages.RecordPublishedAsync(new RecurringMessageRecord
            {
                Name = name,
                CronExpression = "0 * * * *",
                NextOccurrence = name == "pending" ? null : name == "future" ? now.AddHours(1) : now.AddMinutes(-10),
                LastUpdated = now
            }, token);
        }

        await store.RecurringMessages.PauseAsync("paused", now, token);
        var snapshot = await PostgresqlPersistenceMetricsReader.ReadAsync((MessageDatabase<NpgsqlConnection>)store,
            new HashSet<string>(["active", "paused", "pending", "future"], StringComparer.Ordinal), token);

        snapshot.Schedules.Count.ShouldBe(4);
        snapshot.Schedules.Single(item => item.Name == "active").OverdueSeconds.ShouldNotBeNull().ShouldBeInRange(600, 610);
        snapshot.Schedules.Single(item => item.Name == "future").OverdueSeconds.ShouldBe(0);
        snapshot.Schedules.Single(item => item.Name == "pending").OverdueSeconds.ShouldBeNull();
        var paused = snapshot.Schedules.Single(item => item.Name == "paused");
        paused.Paused.ShouldBeTrue();
        paused.OverdueSeconds.ShouldBeNull();
    }
}
