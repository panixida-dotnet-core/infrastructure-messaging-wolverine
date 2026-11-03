using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Policies;

public sealed class MessageBehaviorSelectionTests
{
    [Fact(DisplayName = "Query pipelines exclude domain event publication before behavior construction")]
    public void QueryPipelinesShouldExcludePublicationBehavior()
    {
        var supported = RequestMiddlewareCodeGeneration.TryResolveClosedMiddlewareType(
            typeof(PublishDomainEventsBehavior<,>),
            typeof(TestQuery),
            typeof(Result<TestQueryView>),
            typeof(IAfterMessageBehavior<,>),
            out var closedType);

        supported.ShouldBeFalse();
        closedType.ShouldBeNull();
    }
}
