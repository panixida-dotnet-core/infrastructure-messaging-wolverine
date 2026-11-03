using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Support;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Behaviors;

public sealed class FailBeforeOutboxFlushBehavior<TCommand, TResult>(
    IntegrationTestJournal journal) : IAfterMessageBehavior<TCommand, TResult>
    where TCommand : ICommand<TResult>
    where TResult : Result
{
    public Task AfterAsync(TCommand message, TResult result, CancellationToken cancellationToken)
    {
        journal.Add("behavior.failBeforeFlush");

        throw new PlannedCommandException();
    }
}
