using PANiXiDA.Core.Application.Messaging.EventBus.Handlers;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles;

public sealed class EventHandler : IEventHandler<TestDomainEvent>
{
    public Task HandleAsync(TestDomainEvent @event, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public static Task Handle(TestDomainEvent @event)
    {
        return Task.CompletedTask;
    }

    public static ValueTask HandleValueTask(TestDomainEvent @event)
    {
        return ValueTask.CompletedTask;
    }

    public static void HandleSync(TestDomainEvent @event)
    {
        _ = @event;
    }

    public static string Invalid(TestDomainEvent @event)
    {
        return "unsupported";
    }
}
