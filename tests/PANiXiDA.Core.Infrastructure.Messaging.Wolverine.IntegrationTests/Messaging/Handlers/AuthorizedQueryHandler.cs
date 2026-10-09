using PANiXiDA.Core.Application.Authentication.Abstractions;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Queries;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Handlers;

public sealed class AuthorizedQueryHandler(IntegrationTestJournal journal)
    : IQueryHandler<AuthorizedQuery, Result<string>>, IRequireAuthorization
{
    public Task<Result<string>> HandleAsync(AuthorizedQuery request, CancellationToken cancellationToken)
    {
        journal.Add("handler.authorized-query");
        return Task.FromResult(Result.Success("authorized"));
    }
}
