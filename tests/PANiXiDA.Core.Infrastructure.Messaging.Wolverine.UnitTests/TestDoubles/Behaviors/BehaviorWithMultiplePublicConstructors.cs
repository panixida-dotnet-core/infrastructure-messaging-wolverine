namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class BehaviorWithMultiplePublicConstructors : IBeforeRequestBehavior<TestCommand, Result>
{
    public BehaviorWithMultiplePublicConstructors()
    {
    }

    public BehaviorWithMultiplePublicConstructors(string value)
    {
        Value = value;
    }

    public string? Value { get; }

    public Task BeforeAsync(
        TestCommand request,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
