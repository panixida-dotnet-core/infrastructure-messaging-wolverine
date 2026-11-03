namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Events;

public sealed record PipelineEvent(Guid EventId, bool ThrowHandler = false) : DomainEvent;
