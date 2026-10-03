namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Commands;

public sealed record ScheduleIntegrationMessagesCommand(
    Guid RecordId,
    Guid ScheduledRecordId,
    Guid EventId,
    DateTimeOffset DeliverAt,
    bool ThrowAfterScheduling = false,
    bool ReturnFailure = false) : ICommand<Result>;
