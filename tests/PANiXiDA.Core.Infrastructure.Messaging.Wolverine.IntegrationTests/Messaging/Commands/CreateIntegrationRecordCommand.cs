namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Commands;

public sealed record CreateIntegrationRecordCommand(
    Guid Id,
    string Name) : ICommand<Result>;
