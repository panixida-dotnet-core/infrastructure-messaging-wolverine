using System.Diagnostics.Metrics;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Telemetry;

internal sealed partial class PersistenceMetricsService : BackgroundService
{
    internal const string MeterName = "Wolverine:Persistence";
    private readonly IPersistenceMetricsReader reader;
    private readonly TimeProvider clock;
    private readonly IHostApplicationLifetime lifetime;
    private readonly ILogger<PersistenceMetricsService> logger;
    private CachedSnapshot? snapshot;

    public PersistenceMetricsService(
        IPersistenceMetricsReader reader,
        IMeterFactory meterFactory,
        TimeProvider clock,
        IHostApplicationLifetime lifetime,
        ILogger<PersistenceMetricsService> logger)
    {
        this.reader = reader;
        this.clock = clock;
        this.lifetime = lifetime;
        this.logger = logger;
        var meter = meterFactory.Create(MeterName);
        meter.CreateObservableGauge("wolverine.persistence.dead_letters", () => Observe(value => value.DeadLetters), "{message}");
        meter.CreateObservableGauge("wolverine.persistence.dead_letter.oldest_age", () => Observe(value => value.OldestDeadLetterAgeSeconds), "s");
        meter.CreateObservableGauge("wolverine.persistence.scheduled.due", () => Observe(value => value.ScheduledDue), "{message}");
        meter.CreateObservableGauge("wolverine.persistence.scheduled.overdue", () => Observe(value => value.ScheduledOverdueSeconds), "s");
        meter.CreateObservableGauge("wolverine.persistence.recurring.paused", () => ObserveSchedules(value => value.Paused ? 1 : 0), "{schedule}");
        meter.CreateObservableGauge("wolverine.persistence.recurring.overdue", () => ObserveSchedules(value => value.OverdueSeconds), "s");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        do
        {
            await PollAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var current = await reader.ReadAsync(timeout.Token);
            Volatile.Write(ref snapshot, new CachedSnapshot(clock.GetTimestamp(), current));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Volatile.Write(ref snapshot, null);
            throw;
        }
        catch (Exception exception)
        {
            Volatile.Write(ref snapshot, null);
            // The exception message can contain connection details or SQL; export only its type.
            LogPollFailure(logger, exception.GetType().Name);
        }
    }

    private PersistenceMetricsSnapshot? CurrentSnapshot()
    {
        var current = Volatile.Read(ref snapshot);
        return current is not null && clock.GetElapsedTime(current.Timestamp) < TimeSpan.FromMinutes(2)
            ? current.Value : null;
    }

    private IEnumerable<Measurement<double>> Observe(Func<PersistenceMetricsSnapshot, double?> select)
    {
        if (CurrentSnapshot() is { } current && select(current) is { } value)
        {
            yield return new Measurement<double>(value);
        }
    }

    private IEnumerable<Measurement<double>> ObserveSchedules(Func<RecurringScheduleMetrics, double?> select)
    {
        if (CurrentSnapshot() is not { } current)
        {
            yield break;
        }

        foreach (var schedule in current.Schedules)
        {
            if (select(schedule) is { } value)
            {
                yield return new Measurement<double>(value, new KeyValuePair<string, object?>("schedule.name", schedule.Name));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Wolverine persistence metrics poll failed ({ExceptionType}); queue state is unknown.")]
    private static partial void LogPollFailure(ILogger logger, string exceptionType);

    private sealed record CachedSnapshot(long Timestamp, PersistenceMetricsSnapshot Value);
}
