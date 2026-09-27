using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Behaviors;

public sealed class PersistOutgoingMessagesBehaviorTests
{
    [Theory(DisplayName = "Outbox persistence requires a successful result and an active transaction")]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task PersistenceShouldRequireSuccessfulTransaction(bool success, bool activeTransaction)
    {
        var unitOfWork = new TestUnitOfWork { HasActiveTransaction = activeTransaction };
        var outbox = new TestOutboxDispatcher();
        var behavior = new PersistOutgoingMessagesBehavior<TestCommand, Result>(unitOfWork, outbox);
        var result = success ? Result.Success() : Result.Failure(Error.Failure("Planned failure."));

        await behavior.AfterAsync(new TestCommand(Guid.NewGuid()), result, TestContext.Current.CancellationToken);

        outbox.PersistCallCount.ShouldBe(success && activeTransaction ? 1 : 0);
        outbox.FlushCallCount.ShouldBe(0);
        unitOfWork.CommitTransactionCallCount.ShouldBe(0);
        if (success && activeTransaction)
        {
            outbox.LastPersistCancellationToken.ShouldBe(TestContext.Current.CancellationToken);
        }
    }

    [Fact(DisplayName = "Outbox persistence failures propagate before transaction commit")]
    public async Task PersistenceShouldPropagateFailures()
    {
        var failure = new InvalidOperationException("Persistence failed.");
        var unitOfWork = new TestUnitOfWork { HasActiveTransaction = true };
        var outbox = new TestOutboxDispatcher { PersistException = failure };
        var behavior = new PersistOutgoingMessagesBehavior<TestCommand, Result>(unitOfWork, outbox);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => behavior.AfterAsync(
            new TestCommand(Guid.NewGuid()), Result.Success(), TestContext.Current.CancellationToken));

        exception.ShouldBeSameAs(failure);
        unitOfWork.CommitTransactionCallCount.ShouldBe(0);
        outbox.FlushCallCount.ShouldBe(0);
    }
}
