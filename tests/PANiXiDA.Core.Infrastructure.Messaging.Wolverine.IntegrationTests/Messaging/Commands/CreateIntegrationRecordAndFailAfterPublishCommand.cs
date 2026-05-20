namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Commands;

public sealed record CreateIntegrationRecordAndFailAfterPublishCommand(
    Guid Id,
    string Name) : ICommand<Result>;
