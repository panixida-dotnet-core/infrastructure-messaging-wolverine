using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

using Wolverine.Runtime.Handlers;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Policies;

public sealed class DomainEventPublicationPolicyTests
{
    [Fact(DisplayName = "Native domain event chains receive exactly one publication step")]
    public void ApplyShouldAddPublicationOnceForDomainEvents()
    {
        var policy = new DomainEventPublicationPolicy();
        var chain = new HandlerChain(typeof(TestDomainEvent), new HandlerGraph());

        policy.Apply([chain], null!, null!);
        policy.Apply([chain], null!, null!);

        chain.Postprocessors.ShouldHaveSingleItem().ShouldBeOfType<PublishDomainEventsFrame>();
    }

    [Theory(DisplayName = "Native publication excludes commands, queries and ordinary messages")]
    [InlineData(typeof(TestCommand))]
    [InlineData(typeof(TestQuery))]
    [InlineData(typeof(string))]
    [InlineData(typeof(QueryEvent))]
    public void ApplyShouldSkipNonEventMessages(Type messageType)
    {
        var policy = new DomainEventPublicationPolicy();
        var chain = new HandlerChain(messageType, new HandlerGraph());

        policy.Apply([chain], null!, null!);

        chain.Postprocessors.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Query pipelines exclude domain event publication before behavior construction")]
    public void QueryPipelinesShouldExcludePublicationBehavior()
    {
        var supported = RequestMiddlewareCodeGeneration.TryResolveClosedMiddlewareType(
            typeof(PublishDomainEventsBehavior<,>),
            typeof(TestQuery),
            typeof(Result<TestQueryView>),
            typeof(IAfterRequestBehavior<,>),
            out var closedType);

        supported.ShouldBeFalse();
        closedType.ShouldBeNull();
    }

    private sealed record QueryEvent : DomainEvent, IQuery<Result>;
}
