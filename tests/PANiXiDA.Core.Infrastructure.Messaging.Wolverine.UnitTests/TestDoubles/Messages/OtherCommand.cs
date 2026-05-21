namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Messages;

public sealed record OtherCommand(Guid Id) : ICommand<Result>;
