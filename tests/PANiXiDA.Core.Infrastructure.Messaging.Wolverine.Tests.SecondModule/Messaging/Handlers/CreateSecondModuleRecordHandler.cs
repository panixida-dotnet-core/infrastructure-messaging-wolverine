using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Database.Entities;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Commands;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Events;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Handlers;

public sealed class CreateSecondModuleRecordHandler(
    SecondModuleDbContext dbContext,
    IEventBus eventBus) : ICommandHandler<CreateSecondModuleRecordCommand, Result>
{
    public async Task<Result> HandleAsync(
        CreateSecondModuleRecordCommand command,
        CancellationToken cancellationToken)
    {
        dbContext.Records.Add(new SecondModuleRecord
        {
            Id = command.Id,
            Name = command.Name
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        await eventBus.PublishAsync(
            new SharedModuleEvent(
                command.Id,
                command.Name,
                command.FailSecondModuleEventHandler),
            cancellationToken);

        return Result.Success();
    }
}
