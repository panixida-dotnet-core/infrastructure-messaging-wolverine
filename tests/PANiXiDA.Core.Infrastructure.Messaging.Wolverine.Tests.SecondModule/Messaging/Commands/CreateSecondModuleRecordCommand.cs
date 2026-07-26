namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Commands;

public sealed record CreateSecondModuleRecordCommand(
    Guid Id,
    string Name,
    bool FailSecondModuleEventHandler = false) : ICommand<Result>;
