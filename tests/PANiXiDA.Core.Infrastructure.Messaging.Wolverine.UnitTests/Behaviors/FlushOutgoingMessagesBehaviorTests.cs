using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Behaviors;

public sealed class FlushOutgoingMessagesBehaviorTests
{
    [Fact(DisplayName = "AfterAsync flushes outgoing messages when result succeeds and no transaction is active")]
    public async Task AfterAsyncShouldFlushOutgoingMessagesWhenResultSucceedsAndNoTransactionIsActive()
    {
        var unitOfWork = new TestUnitOfWork();
        var outboxDispatcher = new TestOutboxDispatcher();
        var behavior = new FlushOutgoingMessagesBehavior<TestCommand, Result>(
            unitOfWork,
            outboxDispatcher);

        await behavior.AfterAsync(
            new TestCommand(Guid.NewGuid()),
            Result.Success(),
            TestContext.Current.CancellationToken);

        outboxDispatcher.FlushCallCount.ShouldBe(1);
    }

    [Fact(DisplayName = "AfterAsync does not flush when result failed")]
    public async Task AfterAsyncShouldNotFlushWhenResultFailed()
    {
        var unitOfWork = new TestUnitOfWork();
        var outboxDispatcher = new TestOutboxDispatcher();
        var behavior = new FlushOutgoingMessagesBehavior<TestCommand, Result>(
            unitOfWork,
            outboxDispatcher);

        await behavior.AfterAsync(
            new TestCommand(Guid.NewGuid()),
            Result.Failure(Error.Failure("Failure")),
            TestContext.Current.CancellationToken);

        outboxDispatcher.FlushCallCount.ShouldBe(0);
    }

    [Fact(DisplayName = "AfterAsync does not flush when transaction is active")]
    public async Task AfterAsyncShouldNotFlushWhenTransactionIsActive()
    {
        var unitOfWork = new TestUnitOfWork
        {
            HasActiveTransaction = true
        };
        var outboxDispatcher = new TestOutboxDispatcher();
        var behavior = new FlushOutgoingMessagesBehavior<TestCommand, Result>(
            unitOfWork,
            outboxDispatcher);

        await behavior.AfterAsync(
            new TestCommand(Guid.NewGuid()),
            Result.Success(),
            TestContext.Current.CancellationToken);

        outboxDispatcher.FlushCallCount.ShouldBe(0);
    }
}
