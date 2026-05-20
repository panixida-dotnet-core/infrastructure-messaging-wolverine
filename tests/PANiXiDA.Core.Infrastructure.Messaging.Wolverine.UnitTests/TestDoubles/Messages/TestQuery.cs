namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Messages;

public sealed record TestQuery(Guid Id) : IQuery<Result<TestQueryView>>;
