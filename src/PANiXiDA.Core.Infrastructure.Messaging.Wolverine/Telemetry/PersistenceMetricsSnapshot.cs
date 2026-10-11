namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Telemetry;

internal sealed record PersistenceMetricsSnapshot(
    long DeadLetters,
    double? OldestDeadLetterAgeSeconds,
    long ScheduledDue,
    double? ScheduledOverdueSeconds,
    IReadOnlyList<RecurringScheduleMetrics> Schedules);

internal sealed record RecurringScheduleMetrics(string Name, bool Paused, double? OverdueSeconds);

internal interface IPersistenceMetricsReader
{
    Task<PersistenceMetricsSnapshot> ReadAsync(CancellationToken cancellationToken);
}
