namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class ClosedCommandBeforeBehavior : IBeforeMessageBehavior<TestCommand>
{
    public Task<Result> BeforeAsync(
        TestCommand message,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success());
    }
}
