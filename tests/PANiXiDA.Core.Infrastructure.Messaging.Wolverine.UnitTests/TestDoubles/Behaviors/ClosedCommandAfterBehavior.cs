namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class ClosedCommandAfterBehavior : IAfterRequestBehavior<TestCommand, Result>
{
    public Task AfterAsync(
        TestCommand request,
        Result result,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
