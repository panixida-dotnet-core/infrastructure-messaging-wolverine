namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class ClosedCommandBeforeBehavior : IBeforeRequestBehavior<TestCommand, Result>
{
    public Task<Result> BeforeAsync(
        TestCommand request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success());
    }
}
