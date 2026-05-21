using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Queries;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Views;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Handlers;

public sealed class GetIntegrationRecordHandler(
    IntegrationDbContext dbContext) : IQueryHandler<GetIntegrationRecordQuery, Result<IntegrationRecordView>>
{
    public async Task<Result<IntegrationRecordView>> HandleAsync(
        GetIntegrationRecordQuery query,
        CancellationToken cancellationToken)
    {
        var record = await dbContext
            .Records
            .AsNoTracking()
            .Where(x => x.Id == query.Id)
            .Select(x => new IntegrationRecordView(x.Id, x.Name))
            .SingleAsync(cancellationToken);

        return Result.Success(record);
    }
}
