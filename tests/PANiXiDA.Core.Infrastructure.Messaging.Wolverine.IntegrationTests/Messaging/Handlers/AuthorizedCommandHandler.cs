using PANiXiDA.Core.Application.Authentication.Abstractions;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Commands;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Handlers;

public sealed class AuthorizedCommandHandler(IntegrationTestJournal journal)
    : ICommandHandler<AuthorizedCommand, Result>, IRequireAuthorization
{
    public static IReadOnlyCollection<string> AllPermissions => ["records.create"];
    public static IReadOnlyCollection<string> AnyPermissions => ["records.manage", "records.edit"];

    public Task<Result> HandleAsync(AuthorizedCommand request, CancellationToken cancellationToken)
    {
        journal.Add("handler.authorized");
        return Task.FromResult(Result.Success());
    }
}
