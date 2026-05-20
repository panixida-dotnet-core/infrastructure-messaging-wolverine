namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Events;

public sealed record IntegrationDomainEvent(
    Guid EventId,
    string Name) : DomainEvent;
