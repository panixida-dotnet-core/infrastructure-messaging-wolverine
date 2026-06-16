using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests;

public sealed class WolverineMediatorTests
{
    [Fact(DisplayName = "SendAsync invokes Wolverine message bus with command")]
    public async Task SendAsyncShouldInvokeWolverineMessageBusWithCommand()
    {
        var messageBus = MessageBusProxy.Create(out var proxy);
        var mediator = new WolverineMediator(messageBus);
        var command = new TestCommand(Guid.NewGuid());

        var result = await mediator.SendAsync(
            command,
            TestContext.Current.CancellationToken);

        result.ShouldBeSameAs(proxy.Result);
        proxy.LastMessage.ShouldBeSameAs(command);
        proxy.LastCancellationToken.ShouldBe(TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "QueryAsync invokes Wolverine message bus with query")]
    public async Task QueryAsyncShouldInvokeWolverineMessageBusWithQuery()
    {
        var expected = Result.Success(new TestQueryView(Guid.NewGuid()));
        var messageBus = MessageBusProxy.Create(out var proxy);
        proxy.GenericResult = expected;
        var mediator = new WolverineMediator(messageBus);
        var query = new TestQuery(Guid.NewGuid());

        var result = await mediator.QueryAsync(
            query,
            TestContext.Current.CancellationToken);

        result.ShouldBeSameAs(expected);
        proxy.LastMessage.ShouldBeSameAs(query);
        proxy.LastCancellationToken.ShouldBe(TestContext.Current.CancellationToken);
    }
}
