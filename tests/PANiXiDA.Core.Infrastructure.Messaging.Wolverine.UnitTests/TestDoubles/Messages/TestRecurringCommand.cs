namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Messages;

internal sealed record TestRecurringCommand : ICommand<Result>
{
    public Guid Id { get; init; } = Guid.NewGuid();
}
