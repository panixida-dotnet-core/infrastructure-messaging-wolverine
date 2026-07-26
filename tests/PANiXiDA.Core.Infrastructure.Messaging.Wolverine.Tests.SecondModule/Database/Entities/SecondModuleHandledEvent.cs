namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Database.Entities;

public sealed class SecondModuleHandledEvent
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public string EventType { get; set; } = string.Empty;
}
