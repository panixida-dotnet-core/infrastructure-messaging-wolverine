namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Events;

public sealed record KafkaIntegrationDomainEvent(
    Guid EventId,
    string Name) : DomainEvent;
