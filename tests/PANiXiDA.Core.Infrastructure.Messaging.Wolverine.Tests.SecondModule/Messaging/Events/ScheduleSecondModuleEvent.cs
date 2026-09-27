namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Events;

public sealed record ScheduleSecondModuleEvent(Guid EventId, bool Fail) : DomainEvent;
