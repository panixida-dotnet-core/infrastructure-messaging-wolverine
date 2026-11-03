namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Events;

public sealed record CascadingEvent(Guid EventId) : DomainEvent;

public sealed record AsyncCascadingEvent(Guid EventId) : DomainEvent;

public sealed record ValueTaskCascadingEvent(Guid EventId) : DomainEvent;
