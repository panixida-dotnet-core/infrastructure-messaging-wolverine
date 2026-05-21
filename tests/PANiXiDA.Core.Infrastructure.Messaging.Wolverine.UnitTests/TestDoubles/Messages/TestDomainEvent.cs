namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Messages;

public sealed record TestDomainEvent(Guid EventId) : DomainEvent;
