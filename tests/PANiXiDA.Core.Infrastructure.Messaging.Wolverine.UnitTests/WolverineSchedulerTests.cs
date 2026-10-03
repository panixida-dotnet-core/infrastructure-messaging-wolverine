using PANiXiDA.Core.Application.Messaging.Scheduling;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests;

public sealed class WolverineSchedulerTests
{
    private static readonly DateTimeOffset DeliveryTime = new(2030, 1, 2, 12, 30, 0, TimeSpan.FromHours(4));

    [Theory(DisplayName = "Scheduler preserves message identity and delivery options through the outbox")]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task SchedulerShouldUseOutbox(bool isCommand, bool absoluteTime)
    {
        var proxy = new TestOutboxDispatcher();
        IScheduler scheduler = new WolverineScheduler(proxy);
        object message = isCommand ? new TestCommand(Guid.NewGuid()) : new TestDomainEvent(Guid.NewGuid());

        await ScheduleAsync(scheduler, message, isCommand, absoluteTime, TestContext.Current.CancellationToken);

        proxy.SendCallCount.ShouldBe(isCommand ? 1 : 0);
        proxy.PublishCallCount.ShouldBe(isCommand ? 0 : 1);
        (isCommand ? proxy.LastSentMessage : proxy.LastPublishedEvent).ShouldBeSameAs(message);
        var options = proxy.LastDeliveryOptions.ShouldNotBeNull();
        if (absoluteTime)
        {
            options.ScheduledTime.ShouldBe(DeliveryTime);
        }
        else
        {
            options.ScheduleDelay.ShouldBe(TimeSpan.FromMinutes(15));
        }

        proxy.LastDispatchCancellationToken.ShouldBe(TestContext.Current.CancellationToken);
        proxy.SaveChangesCallCount.ShouldBe(0);
        proxy.FlushCallCount.ShouldBe(0);
    }

    [Theory(DisplayName = "Scheduler propagates outbox failures")]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task SchedulerShouldPropagateOutboxFailures(bool isCommand, bool absoluteTime)
    {
        var proxy = new TestOutboxDispatcher();
        IScheduler scheduler = new WolverineScheduler(proxy);
        object message = isCommand ? new TestCommand(Guid.NewGuid()) : new TestDomainEvent(Guid.NewGuid());
        var failure = new InvalidOperationException("Outbox failed.");
        proxy.DispatchException = failure;

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            ScheduleAsync(scheduler, message, isCommand, absoluteTime, TestContext.Current.CancellationToken));

        exception.ShouldBeSameAs(failure);
    }

    [Theory(DisplayName = "Scheduler rejects negative delays and accepts zero")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SchedulerShouldValidateDelay(bool isCommand)
    {
        var proxy = new TestOutboxDispatcher();
        var scheduler = new WolverineScheduler(proxy);
        var command = new TestCommand(Guid.NewGuid());
        var @event = new TestDomainEvent(Guid.NewGuid());
        var token = TestContext.Current.CancellationToken;
        Task schedule(TimeSpan delay) => isCommand
            ? scheduler.ScheduleAsync(command, delay, token)
            : scheduler.ScheduleAsync(@event, delay, token);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => schedule(TimeSpan.FromTicks(-1)));
        proxy.SendCallCount.ShouldBe(0);
        proxy.PublishCallCount.ShouldBe(0);
        await schedule(TimeSpan.Zero);

        proxy.LastDeliveryOptions.ShouldNotBeNull().ScheduleDelay.ShouldBe(TimeSpan.Zero);
    }

    private static Task ScheduleAsync(
        IScheduler scheduler,
        object message,
        bool isCommand,
        bool absoluteTime,
        CancellationToken cancellationToken)
    {
        if (isCommand)
        {
            var command = (ICommand<Result>)message;
            return absoluteTime
                ? scheduler.ScheduleAtAsync(command, DeliveryTime, cancellationToken)
                : scheduler.ScheduleAsync(command, TimeSpan.FromMinutes(15), cancellationToken);
        }

        var @event = (DomainEvent)message;
        return absoluteTime
            ? scheduler.ScheduleAtAsync(@event, DeliveryTime, cancellationToken)
            : scheduler.ScheduleAsync(@event, TimeSpan.FromMinutes(15), cancellationToken);
    }
}
