using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Policies;

public sealed class MessageBehaviorSelectionTests
{
    [Fact(DisplayName = "Behavior selection follows generated constraints without publisher-specific query exclusions")]
    public void QueryPipelinesShouldSelectPublicationBehaviorThroughItsContract()
    {
        var supported = RequestMiddlewareCodeGeneration.TryResolveClosedMiddlewareType(
            typeof(PublishDomainEventsBehavior<,>),
            typeof(TestQuery),
            typeof(Result<TestQueryView>),
            typeof(IAfterMessageBehavior<,>),
            out var closedType);

        supported.ShouldBeTrue();
        closedType.ShouldBe(typeof(PublishDomainEventsBehavior<TestQuery, Result<TestQueryView>>));
    }
}
