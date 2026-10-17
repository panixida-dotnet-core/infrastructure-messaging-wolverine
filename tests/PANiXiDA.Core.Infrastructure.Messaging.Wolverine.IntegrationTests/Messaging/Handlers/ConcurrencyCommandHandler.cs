using PANiXiDA.Core.Application.Persistence.Errors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database.Entities;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Commands;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Handlers;

public sealed class ConcurrencyCommandHandler(IntegrationDbContext dbContext) : ICommandHandler<ConcurrencyCommand, Result>
{
    public async Task<Result> HandleAsync(ConcurrencyCommand command, CancellationToken cancellationToken)
    {
        dbContext.Records.Add(new IntegrationRecord { Id = command.Id, Name = "Rolled back on conflict" });
        await dbContext.SaveChangesAsync(cancellationToken);
        throw new ConcurrencyException("Internal conflict details");
    }
}
