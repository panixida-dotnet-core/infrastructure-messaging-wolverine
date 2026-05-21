namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Messages;

public record BaseCommand(Guid Id) : ICommand<Result>;
