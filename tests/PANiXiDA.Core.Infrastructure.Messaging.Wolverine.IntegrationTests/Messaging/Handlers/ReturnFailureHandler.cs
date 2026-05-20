using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database.Entities;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Commands;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Handlers;

public sealed class ReturnFailureHandler(
    IntegrationDbContext dbContext,
    IntegrationTestJournal journal) : ICommandHandler<ReturnFailureCommand, Result>
{
    public Task<Result> HandleAsync(
        ReturnFailureCommand command,
        CancellationToken cancellationToken)
    {
        journal.Add("handler.command");

        dbContext.Records.Add(new IntegrationRecord
        {
            Id = command.Id,
            Name = command.Name,
        });

        return Task.FromResult(Result.Failure(Error.Failure("Planned failure.")));
    }
}
