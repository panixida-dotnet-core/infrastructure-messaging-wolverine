using PANiXiDA.Core.Application.Persistence.Errors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Queries;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Views;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Handlers;

public sealed class ConcurrencyQueryHandler : IQueryHandler<ConcurrencyQuery, Result<IntegrationRecordView>>
{
    public Task<Result<IntegrationRecordView>> HandleAsync(ConcurrencyQuery query, CancellationToken cancellationToken)
    {
        throw new ConcurrencyException("Internal conflict details");
    }
}
