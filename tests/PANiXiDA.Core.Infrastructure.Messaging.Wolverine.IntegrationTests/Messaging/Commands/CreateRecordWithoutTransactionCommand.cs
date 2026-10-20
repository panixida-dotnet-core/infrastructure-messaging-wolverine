namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Commands;

public sealed record CreateRecordWithoutTransactionCommand(Guid Id, bool Fail, bool SaveImmediately) : ICommand<Result>;
