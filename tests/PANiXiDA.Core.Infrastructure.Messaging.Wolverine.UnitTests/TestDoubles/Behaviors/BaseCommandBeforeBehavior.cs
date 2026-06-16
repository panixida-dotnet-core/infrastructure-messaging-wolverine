namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class BaseCommandBeforeBehavior : IBeforeRequestBehavior<BaseCommand, Result>
{
    public Task<Result> BeforeAsync(
        BaseCommand request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success());
    }
}
