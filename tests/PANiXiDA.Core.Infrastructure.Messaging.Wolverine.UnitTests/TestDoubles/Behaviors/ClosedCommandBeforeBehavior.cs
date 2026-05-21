namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class ClosedCommandBeforeBehavior : IBeforeRequestBehavior<TestCommand, Result>
{
    public Task BeforeAsync(
        TestCommand request,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
