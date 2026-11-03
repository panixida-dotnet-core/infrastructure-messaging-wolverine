namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class BehaviorWithMultiplePublicConstructors : IBeforeMessageBehavior<TestCommand>
{
    public BehaviorWithMultiplePublicConstructors()
    {
    }

    public BehaviorWithMultiplePublicConstructors(string value)
    {
        Value = value;
    }

    public string? Value { get; }

    public Task<Result> BeforeAsync(
        TestCommand message,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success());
    }
}
