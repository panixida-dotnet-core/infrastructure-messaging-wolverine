namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Commands;

public sealed record ConcurrencyCommand(Guid Id) : ICommand<Result>;
