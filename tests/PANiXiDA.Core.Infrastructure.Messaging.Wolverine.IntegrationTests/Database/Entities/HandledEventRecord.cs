namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database.Entities;

public sealed class HandledEventRecord
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public string Name { get; set; } = string.Empty;
}
