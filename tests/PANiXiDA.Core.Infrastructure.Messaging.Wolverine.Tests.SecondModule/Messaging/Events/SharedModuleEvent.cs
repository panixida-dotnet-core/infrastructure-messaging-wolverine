namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Events;

public sealed record SharedModuleEvent(
    Guid EventId,
    string Name,
    bool FailSecondModuleHandler,
    bool FailFirstModuleHandler = false,
    bool FailFirstModuleSave = false) : DomainEvent;
