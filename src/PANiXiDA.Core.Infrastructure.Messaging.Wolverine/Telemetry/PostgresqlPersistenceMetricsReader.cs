using Npgsql;

using Wolverine.RDBMS;
using Wolverine.Runtime;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Telemetry;

internal sealed class PostgresqlPersistenceMetricsReader(IWolverineRuntime runtime) : IPersistenceMetricsReader
{
    public Task<PersistenceMetricsSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        return ReadAsync((MessageDatabase<NpgsqlConnection>)runtime.Storage,
            runtime.Options.Schedules.Select(schedule => schedule.Name).ToHashSet(StringComparer.Ordinal), cancellationToken);
    }

    internal static async Task<PersistenceMetricsSnapshot> ReadAsync(
        MessageDatabase<NpgsqlConnection> store,
        IReadOnlySet<string> registeredSchedules,
        CancellationToken cancellationToken)
    {
        using var identifierBuilder = new NpgsqlCommandBuilder();
        var schema = identifierBuilder.QuoteIdentifier(store.SchemaName);
        // Aggregates only: never load envelope bodies, exception text, or recipient addresses.
        // sent_at is the original send time, not the time the envelope entered the DLQ.
        await using var command = store.CreateCommand($"""
            select dlq.total, dlq.oldest_age, scheduled.total, scheduled.overdue, now()
            from (
                select count(*) as total,
                    case when count(*) = 0 then 0::double precision
                         when count(sent_at) = count(*) then greatest(0, extract(epoch from now() - min(sent_at)))::double precision
                         else null end as oldest_age
                from {schema}.{DatabaseConstants.DeadLetterTable}
            ) dlq
            cross join (
                select count(*) as total,
                    coalesce(greatest(0, extract(epoch from now() - min(execution_time))), 0)::double precision as overdue
                from {schema}.{DatabaseConstants.IncomingTable}
                where status = 'Scheduled' and execution_time <= now()
            ) scheduled
            """);
        command.CommandTimeout = 10;
        long deadLetters;
        double? oldestAge;
        long scheduledDue;
        double overdue;
        DateTimeOffset observedAt;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            deadLetters = reader.GetInt64(0);
            oldestAge = reader.IsDBNull(1) ? null : reader.GetDouble(1);
            scheduledDue = reader.GetInt64(2);
            overdue = reader.GetDouble(3);
            observedAt = reader.GetFieldValue<DateTimeOffset>(4);
        }

        var records = await store.RecurringMessages.LoadAllAsync(cancellationToken);
        var schedules = records
            .Where(record => registeredSchedules.Contains(record.Name))
            .Select(record => new RecurringScheduleMetrics(
                record.Name,
                record.Paused,
                !record.Paused && record.NextOccurrence is { } next
                    ? Math.Max(0, (observedAt - next).TotalSeconds) : null))
            .ToArray();

        return new PersistenceMetricsSnapshot(deadLetters, oldestAge, scheduledDue, overdue, schedules);
    }
}
