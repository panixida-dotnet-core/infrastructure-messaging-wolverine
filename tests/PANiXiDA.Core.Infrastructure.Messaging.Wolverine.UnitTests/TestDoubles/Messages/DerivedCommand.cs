namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Messages;

public sealed record DerivedCommand(Guid Id) : BaseCommand(Id);
