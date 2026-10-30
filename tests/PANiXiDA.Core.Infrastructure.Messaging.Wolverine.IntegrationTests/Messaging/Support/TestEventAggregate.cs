using PANiXiDA.Core.Domain.AggregateRoots;
using PANiXiDA.Core.Domain.Identifiers;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Support;

internal readonly record struct TestEventAggregateId(Guid Value) : IStronglyTypedId;

internal sealed class TestEventAggregate(Guid id) : AggregateRoot<TestEventAggregateId>(new(id))
{
    internal void Raise(DomainEvent domainEvent)
    {
        AddDomainEvent(domainEvent);
    }
}
