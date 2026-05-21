using PANiXiDA.Core.Domain.AggregateRoots;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Support;

public sealed class TestAggregateTracker : IAggregateTracker
{
    private readonly List<IAggregateRoot> aggregateRoots = [];

    public void Track(IAggregateRoot aggregateRoot)
    {
        aggregateRoots.Add(aggregateRoot);
    }

    public IReadOnlyCollection<IAggregateRoot> GetAll()
    {
        return aggregateRoots;
    }

    public void Clear()
    {
        aggregateRoots.Clear();
    }
}
