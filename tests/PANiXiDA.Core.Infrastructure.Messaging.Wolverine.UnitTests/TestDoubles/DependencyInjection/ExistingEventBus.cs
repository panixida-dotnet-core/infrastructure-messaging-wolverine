namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.DependencyInjection;

public sealed class ExistingEventBus : IEventBus
{
    public Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : DomainEvent
    {
        throw new NotSupportedException();
    }
}
