using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class WolverineRequestBehaviorConfigurationTests
{
    [Fact(DisplayName = "CreateDefault registers the built-in request pipeline")]
    public void CreateDefaultShouldRegisterBuiltInRequestPipeline()
    {
        var configuration = WolverineRequestBehaviorConfiguration.CreateDefault();

        var registry = configuration.Build();

        registry.BeforeMiddlewareTypes.Should().Equal(typeof(BeginTransactionBehavior<,>));
        registry.AfterMiddlewareTypes.Should().Equal(
            typeof(PublishDomainEventsBehavior<,>),
            typeof(CommitTransactionBehavior<,>),
            typeof(FlushOutgoingMessagesBehavior<,>));
        registry.FinallyMiddlewareTypes.Should().Equal(typeof(CleanupTransactionBehavior<,>));
    }

    [Fact(DisplayName = "Stage configuration appends and inserts behavior relative to anchors")]
    public void StageConfigurationShouldAppendAndInsertBehaviorRelativeToAnchors()
    {
        var configuration = WolverineRequestBehaviorConfiguration.CreateDefault();

        configuration.Before.InsertAfter(
            typeof(TestBeforeBehavior<,>),
            typeof(BeginTransactionBehavior<,>));
        configuration.After.InsertBefore(
            typeof(TestAfterBehavior<,>),
            typeof(CommitTransactionBehavior<,>));
        configuration.Finally.InsertAfter(
            typeof(TestFinallyBehavior<,>),
            typeof(CleanupTransactionBehavior<,>));

        var registry = configuration.Build();

        registry.BeforeMiddlewareTypes.Should().Equal(
            typeof(BeginTransactionBehavior<,>),
            typeof(TestBeforeBehavior<,>));
        registry.AfterMiddlewareTypes.Should().Equal(
            typeof(PublishDomainEventsBehavior<,>),
            typeof(TestAfterBehavior<,>),
            typeof(CommitTransactionBehavior<,>),
            typeof(FlushOutgoingMessagesBehavior<,>));
        registry.FinallyMiddlewareTypes.Should().Equal(
            typeof(CleanupTransactionBehavior<,>),
            typeof(TestFinallyBehavior<,>));
    }

    [Fact(DisplayName = "Stage configuration rejects missing anchor behavior")]
    public void StageConfigurationShouldRejectMissingAnchorBehavior()
    {
        var configuration = WolverineRequestBehaviorConfiguration.CreateDefault();

        var act = () => configuration.Before.InsertBefore(
            typeof(TestBeforeBehavior<,>),
            typeof(SecondBeforeBehavior<,>));

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Before behavior '*' was not registered.");
    }

    [Fact(DisplayName = "Generic stage methods delegate to type-based methods")]
    public void GenericStageMethodsShouldDelegateToTypeBasedMethods()
    {
        var configuration = WolverineRequestBehaviorConfiguration.CreateDefault();

        configuration.Before.Add<TestBeforeBehavior<TestCommand, Result>>();
        configuration.Before.InsertBefore
            <SecondBeforeBehavior<TestCommand, Result>,
            TestBeforeBehavior<TestCommand, Result>>();
        configuration.Before.InsertAfter
            <ClosedCommandBeforeBehavior,
            SecondBeforeBehavior<TestCommand, Result>>();

        var registry = configuration.Build();

        registry.BeforeMiddlewareTypes.Should().ContainInOrder(
            typeof(SecondBeforeBehavior<TestCommand, Result>),
            typeof(ClosedCommandBeforeBehavior),
            typeof(TestBeforeBehavior<TestCommand, Result>));
    }
}
