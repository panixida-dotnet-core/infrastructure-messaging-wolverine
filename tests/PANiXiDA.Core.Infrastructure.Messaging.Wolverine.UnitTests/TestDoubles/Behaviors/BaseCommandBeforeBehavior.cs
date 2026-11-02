namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class BaseCommandBeforeBehavior : IBeforeMessageBehavior<BaseCommand>
{
    public Task<Result> BeforeAsync(
        BaseCommand message,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success());
    }
}
