namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Commands;

public sealed record ReturnFailureCommand(
    Guid Id,
    string Name) : ICommand<Result>;
