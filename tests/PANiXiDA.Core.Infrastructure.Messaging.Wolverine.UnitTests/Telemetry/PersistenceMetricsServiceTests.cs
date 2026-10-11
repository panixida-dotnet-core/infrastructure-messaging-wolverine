using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Telemetry;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Telemetry;

public sealed class PersistenceMetricsServiceTests
{
    [Fact(DisplayName = "The poller waits for host startup and stops promptly with the host")]
    public async Task PollerShouldFollowHostLifetime()
    {
        using var harness = new MetricsHarness();
        var token = TestContext.Current.CancellationToken;
        await harness.Service.StartAsync(token);
        harness.Reader.Observed.Task.IsCompleted.ShouldBeFalse();

        harness.Lifetime.Start();
        await harness.Reader.Observed.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        await harness.Service.StopAsync(token);

        harness.Service.ExecuteTask.ShouldNotBeNull().IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact(DisplayName = "Stopping before host startup completes normally without polling storage")]
    public async Task PollerShouldStopWhileWaitingForStartup()
    {
        using var harness = new MetricsHarness();
        var token = TestContext.Current.CancellationToken;
        await harness.Service.StartAsync(token);
        await harness.Lifetime.Observed.Task.WaitAsync(TimeSpan.FromSeconds(10), token);

        await harness.Service.StopAsync(token);

        harness.Reader.Observed.Task.IsCompleted.ShouldBeFalse();
        harness.Service.ExecuteTask.ShouldNotBeNull().IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact(DisplayName = "A timed out database read leaves queue state unknown without stopping the host")]
    public async Task PollerShouldContainReadTimeouts()
    {
        using var harness = new MetricsHarness();
        harness.Reader.Error = new OperationCanceledException("Read timed out");

        await harness.Service.PollAsync(TestContext.Current.CancellationToken);

        harness.Collect().ShouldBeEmpty();
    }

    [Fact(DisplayName = "Persistence gauges distinguish unknown state from an empty queue and expire old samples")]
    public async Task GaugesShouldDistinguishEmptyAndUnknown()
    {
        using var harness = new MetricsHarness();
        harness.Collect().ShouldBeEmpty();

        await harness.Service.PollAsync(TestContext.Current.CancellationToken);

        harness.Collect().Count.ShouldBe(4);
        harness.Collect().Values.ShouldAllBe(value => value == 0);
        harness.Clock.Advance(TimeSpan.FromMinutes(2));
        harness.Collect().ShouldBeEmpty();
    }

    [Fact(DisplayName = "Persistence gauges preserve counts, nullable ages and bounded schedule labels")]
    public async Task GaugesShouldExportQueueAndScheduleValues()
    {
        using var harness = new MetricsHarness();
        harness.Reader.Result = new(12, null, 3, 91,
            [new("cleanup", false, 123), new("paused", true, null)]);

        await harness.Service.PollAsync(TestContext.Current.CancellationToken);
        var values = harness.Collect();

        values["wolverine.persistence.dead_letters"].ShouldBe(12);
        values.ShouldNotContainKey("wolverine.persistence.dead_letter.oldest_age");
        values["wolverine.persistence.scheduled.due"].ShouldBe(3);
        values["wolverine.persistence.scheduled.overdue"].ShouldBe(91);
        values["wolverine.persistence.recurring.paused|cleanup"].ShouldBe(0);
        values["wolverine.persistence.recurring.overdue|cleanup"].ShouldBe(123);
        values["wolverine.persistence.recurring.paused|paused"].ShouldBe(1);
        values.ShouldNotContainKey("wolverine.persistence.recurring.overdue|paused");
    }

    [Fact(DisplayName = "Failed polls remove cached values and a later successful poll recovers")]
    public async Task PollFailureShouldNotPublishStaleOrZeroValues()
    {
        using var harness = new MetricsHarness();
        harness.Reader.Result = new(2, 600, 0, 0, []);
        await harness.Service.PollAsync(TestContext.Current.CancellationToken);
        harness.Collect()["wolverine.persistence.dead_letter.oldest_age"].ShouldBe(600);

        harness.Reader.Error = new InvalidOperationException("sensitive connection details");
        await harness.Service.PollAsync(TestContext.Current.CancellationToken);
        harness.Collect().ShouldBeEmpty();

        harness.Reader.Error = null;
        await harness.Service.PollAsync(TestContext.Current.CancellationToken);
        harness.Collect()["wolverine.persistence.dead_letters"].ShouldBe(2);
    }

    [Fact(DisplayName = "Host cancellation is propagated and clears persistence gauges")]
    public async Task PollShouldRespectCancellation()
    {
        using var harness = new MetricsHarness();
        await harness.Service.PollAsync(TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => harness.Service.PollAsync(cancellation.Token));

        harness.Collect().ShouldBeEmpty();
    }

    private sealed class MetricsHarness : IDisposable
    {
        private readonly ServiceProvider provider;
        private readonly MeterListener listener = new();
        private readonly Dictionary<string, double> values = [];
        public FakeReader Reader { get; } = new();
        public ManualClock Clock { get; } = new();
        public TestLifetime Lifetime { get; } = new();
        public PersistenceMetricsService Service { get; }

        public MetricsHarness()
        {
            provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
            listener.InstrumentPublished = (instrument, observer) =>
            {
                if (instrument.Meter.Name == PersistenceMetricsService.MeterName)
                {
                    observer.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                var name = instrument.Name;
                foreach (var tag in tags)
                {
                    tag.Key.ShouldBe("schedule.name");
                    name += "|" + tag.Value;
                }

                values[name] = value;
            });
            listener.Start();
            Service = new PersistenceMetricsService(Reader, provider.GetRequiredService<IMeterFactory>(),
                Clock, Lifetime, NullLogger<PersistenceMetricsService>.Instance);
        }

        public Dictionary<string, double> Collect()
        {
            values.Clear();
            listener.RecordObservableInstruments();
            return new Dictionary<string, double>(values);
        }

        public void Dispose()
        {
            Service.Dispose();
            Lifetime.Dispose();
            listener.Dispose();
            provider.Dispose();
        }
    }

    private sealed class FakeReader : IPersistenceMetricsReader
    {
        public PersistenceMetricsSnapshot Result { get; set; } = new(0, 0, 0, 0, []);
        public Exception? Error { get; set; }
        public TaskCompletionSource Observed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<PersistenceMetricsSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Observed.TrySetResult();
            return Error is { } error ? Task.FromException<PersistenceMetricsSnapshot>(error) : Task.FromResult(Result);
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(TimeSpan duration) => timestamp += duration.Ticks;
    }

    private sealed class TestLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource started = new();
        public TaskCompletionSource Observed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken ApplicationStarted
        {
            get
            {
                Observed.TrySetResult();
                return started.Token;
            }
        }
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
        public void Start() => started.Cancel();
        public void Dispose() => started.Dispose();
    }
}
