namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Commands;

public sealed record CreateIntegrationRecordAndPublishEventCommand(
    Guid Id,
    string Name) : ICommand<Result>;
