using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Views;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Queries;

public sealed record GetIntegrationRecordQuery(Guid Id) : IQuery<Result<IntegrationRecordView>>;
