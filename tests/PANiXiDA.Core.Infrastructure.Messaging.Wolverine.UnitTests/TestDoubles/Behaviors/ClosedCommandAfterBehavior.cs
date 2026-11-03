namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class ClosedCommandAfterBehavior : IAfterMessageBehavior<TestCommand, Result>
{
    public Task AfterAsync(
        TestCommand message,
        Result result,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
