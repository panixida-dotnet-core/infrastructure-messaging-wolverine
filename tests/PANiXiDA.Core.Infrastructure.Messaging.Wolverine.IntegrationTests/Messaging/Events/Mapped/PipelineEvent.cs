namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Events.Mapped;

public sealed record PipelineEvent(Guid EventId) : DomainEvent;
