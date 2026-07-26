namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Events;

public sealed record SharedModuleFollowUpEvent(
    Guid EventId) : DomainEvent;
