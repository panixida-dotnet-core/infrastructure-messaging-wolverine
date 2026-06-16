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

        registry.BeforeMiddlewareTypes.ShouldBe(new[]
        {
            typeof(ValidationBehavior<,>),
            typeof(BeginTransactionBehavior<,>)
        });
        registry.AfterMiddlewareTypes.ShouldBe(new[]
        {
            typeof(PublishDomainEventsBehavior<,>),
            typeof(CommitTransactionBehavior<,>),
            typeof(FlushOutgoingMessagesBehavior<,>)
        });
        registry.FinallyMiddlewareTypes.ShouldBe(new[] { typeof(CleanupTransactionBehavior<,>) });
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

        registry.BeforeMiddlewareTypes.ShouldBe(new[]
        {
            typeof(ValidationBehavior<,>),
            typeof(BeginTransactionBehavior<,>),
            typeof(TestBeforeBehavior<,>)
        });
        registry.AfterMiddlewareTypes.ShouldBe(new[]
        {
            typeof(PublishDomainEventsBehavior<,>),
            typeof(TestAfterBehavior<,>),
            typeof(CommitTransactionBehavior<,>),
            typeof(FlushOutgoingMessagesBehavior<,>)
        });
        registry.FinallyMiddlewareTypes.ShouldBe(new[]
        {
            typeof(CleanupTransactionBehavior<,>),
            typeof(TestFinallyBehavior<,>)
        });
    }

    [Fact(DisplayName = "Stage configuration rejects missing anchor behavior")]
    public void StageConfigurationShouldRejectMissingAnchorBehavior()
    {
        var configuration = WolverineRequestBehaviorConfiguration.CreateDefault();

        var act = () => configuration.Before.InsertBefore(
            typeof(TestBeforeBehavior<,>),
            typeof(SecondBeforeBehavior<,>));

        var exception = Should.Throw<InvalidOperationException>(act);

        exception.Message.ShouldStartWith("Before behavior '");
        exception.Message.ShouldEndWith("' was not registered.");
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

        registry.BeforeMiddlewareTypes.ShouldBe(new[]
        {
            typeof(ValidationBehavior<,>),
            typeof(BeginTransactionBehavior<,>),
            typeof(SecondBeforeBehavior<TestCommand, Result>),
            typeof(ClosedCommandBeforeBehavior),
            typeof(TestBeforeBehavior<TestCommand, Result>)
        });
    }
}
