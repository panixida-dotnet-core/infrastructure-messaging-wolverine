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

        result.Should().BeSameAs(proxy.Result);
        proxy.LastMessage.Should().BeSameAs(command);
        proxy.LastCancellationToken.Should().Be(TestContext.Current.CancellationToken);
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

        result.Should().BeSameAs(expected);
        proxy.LastMessage.Should().BeSameAs(query);
        proxy.LastCancellationToken.Should().Be(TestContext.Current.CancellationToken);
    }
}
