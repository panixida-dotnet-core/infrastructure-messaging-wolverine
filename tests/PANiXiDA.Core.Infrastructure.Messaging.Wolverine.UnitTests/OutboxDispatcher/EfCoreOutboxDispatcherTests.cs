using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.OutboxDispatcher;

using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.OutboxDispatcher;

public sealed class EfCoreOutboxDispatcherTests
{
    [Fact(DisplayName = "PublishAsync delegates event to EF Core outbox")]
    public async Task PublishAsyncShouldDelegateEventToEfCoreOutbox()
    {
        var outbox = DbContextOutboxProxy<TestDbContext>.Create(out var proxy);
        var dispatcher = new EfCoreOutboxDispatcher<TestDbContext>(outbox);
        var domainEvent = new TestDomainEvent(Guid.NewGuid());

        await dispatcher.PublishAsync(
            domainEvent,
            TestContext.Current.CancellationToken);

        proxy.PublishCallCount.ShouldBe(1);
        proxy.LastPublishedMessage.ShouldBeSameAs(domainEvent);
    }

    [Theory(DisplayName = "PublishAsync propagates synchronous and asynchronous outbox failures")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PublishAsyncShouldPropagateOutboxFailures(bool throwsSynchronously)
    {
        var outbox = DbContextOutboxProxy<TestDbContext>.Create(out var proxy);
        var dispatcher = new EfCoreOutboxDispatcher<TestDbContext>(outbox);
        var domainEvent = new TestDomainEvent(Guid.NewGuid());
        var failure = new InvalidOperationException("Outbox failed.");
        if (throwsSynchronously)
        {
            proxy.SynchronousPublishException = failure;
        }
        else
        {
            proxy.DispatchException = failure;
        }

        InvalidOperationException exception;
        if (throwsSynchronously)
        {
            exception = Should.Throw<InvalidOperationException>(() =>
            {
                _ = dispatcher.PublishAsync(domainEvent, TestContext.Current.CancellationToken);
            });
        }
        else
        {
            var task = dispatcher.PublishAsync(domainEvent, TestContext.Current.CancellationToken);
            exception = await Should.ThrowAsync<InvalidOperationException>(() => task);
        }

        exception.ShouldBeSameAs(failure);
        proxy.PublishCallCount.ShouldBe(1);
        proxy.LastPublishedMessage.ShouldBeSameAs(domainEvent);
    }

    [Theory(DisplayName = "Dispatcher preserves message identity and delivery options without saving or flushing")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DispatchShouldPreserveDeliveryOptions(bool isCommand)
    {
        var outbox = DbContextOutboxProxy<TestDbContext>.Create(out var proxy);
        var dispatcher = new EfCoreOutboxDispatcher<TestDbContext>(outbox);
        var command = new TestCommand(Guid.NewGuid());
        var @event = new TestDomainEvent(Guid.NewGuid());
        var options = new DeliveryOptions { ScheduleDelay = TimeSpan.FromMinutes(15) };
        var token = TestContext.Current.CancellationToken;

        if (isCommand)
        {
            await dispatcher.SendAsync(command, options, token);
        }
        else
        {
            await dispatcher.PublishAsync(@event, options, token);
        }

        proxy.SendCallCount.ShouldBe(isCommand ? 1 : 0);
        proxy.PublishCallCount.ShouldBe(isCommand ? 0 : 1);
        (isCommand ? proxy.LastSentMessage : proxy.LastPublishedMessage)
            .ShouldBeSameAs(isCommand ? (object)command : @event);
        proxy.LastDeliveryOptions.ShouldBeSameAs(options);
        proxy.FlushCallCount.ShouldBe(0);
    }

    [Theory(DisplayName = "Dispatcher propagates failures from the transactional outbox")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DispatchShouldPropagateOutboxFailures(bool isCommand)
    {
        var outbox = DbContextOutboxProxy<TestDbContext>.Create(out var proxy);
        var dispatcher = new EfCoreOutboxDispatcher<TestDbContext>(outbox);
        var failure = new InvalidOperationException("Outbox failed.");
        proxy.DispatchException = failure;
        var options = new DeliveryOptions();
        var token = TestContext.Current.CancellationToken;

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => isCommand
            ? dispatcher.SendAsync(new TestCommand(Guid.NewGuid()), options, token)
            : dispatcher.PublishAsync(new TestDomainEvent(Guid.NewGuid()), options, token));

        exception.ShouldBeSameAs(failure);
    }

    [Fact(DisplayName = "FlushAsync delegates flush to EF Core outbox")]
    public async Task FlushAsyncShouldDelegateFlushToEfCoreOutbox()
    {
        var outbox = DbContextOutboxProxy<TestDbContext>.Create(out var proxy);
        var dispatcher = new EfCoreOutboxDispatcher<TestDbContext>(outbox);

        await dispatcher.FlushAsync(TestContext.Current.CancellationToken);

        proxy.FlushCallCount.ShouldBe(1);
    }
}
