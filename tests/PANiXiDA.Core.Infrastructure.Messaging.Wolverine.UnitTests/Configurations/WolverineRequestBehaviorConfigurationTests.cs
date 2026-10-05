using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class WolverineRequestBehaviorConfigurationTests
{
    [Theory(DisplayName = "Query pipelines exclude domain event publication, transactions, and outbox behaviors")]
    [InlineData(false)]
    [InlineData(true)]
    public void QueryPipelineShouldExcludeCommandBehaviors(bool useModules)
    {
        // Arrange
        var configuration = useModules
            ? WolverineRequestBehaviorConfiguration.CreateModularDefault()
            : WolverineRequestBehaviorConfiguration.CreateDefault();
        var registry = configuration.Build();

        // Act
        var before = RequestMiddlewareDescriptor.Resolve(
            typeof(TestQuery), typeof(Result<TestQueryView>),
            typeof(IBeforeRequestBehavior<,>), registry.BeforeMiddlewareTypes);
        var after = RequestMiddlewareDescriptor.Resolve(
            typeof(TestQuery), typeof(Result<TestQueryView>),
            typeof(IAfterRequestBehavior<,>), registry.AfterMiddlewareTypes);
        var finallyBehaviors = RequestMiddlewareDescriptor.Resolve(
            typeof(TestQuery), typeof(Result<TestQueryView>),
            typeof(IFinallyRequestBehavior<,>), registry.FinallyMiddlewareTypes);

        // Assert
        Type[] expectedBefore = useModules
            ? [typeof(ActivateWolverineModuleBehavior<TestQuery, Result<TestQueryView>>),
                typeof(ValidationBehavior<TestQuery, Result<TestQueryView>>)]
            : [typeof(ValidationBehavior<TestQuery, Result<TestQueryView>>)];
        Type[] expectedFinally = useModules
            ? [typeof(DeactivateWolverineModuleBehavior<TestQuery, Result<TestQueryView>>)]
            : [];
        before.Select(behavior => behavior.Type).ShouldBe(expectedBefore);
        after.ShouldBeEmpty();
        finallyBehaviors.Select(behavior => behavior.Type).ShouldBe(expectedFinally);
    }

    [Theory(DisplayName = "Command pipelines retain domain event publication and transactional outbox behaviors")]
    [InlineData(false)]
    [InlineData(true)]
    public void CommandPipelineShouldRetainPublicationAndOutboxBehaviors(bool useModules)
    {
        // Arrange
        var configuration = useModules
            ? WolverineRequestBehaviorConfiguration.CreateModularDefault()
            : WolverineRequestBehaviorConfiguration.CreateDefault();
        var registry = configuration.Build();

        // Act
        var after = RequestMiddlewareDescriptor.Resolve(
            typeof(TestCommand), typeof(Result),
            typeof(IAfterRequestBehavior<,>), registry.AfterMiddlewareTypes);

        // Assert
        after.Select(behavior => behavior.Type).ShouldBe(
        [
            typeof(PublishDomainEventsBehavior<TestCommand, Result>),
            typeof(PersistOutgoingMessagesBehavior<TestCommand, Result>),
            typeof(CommitTransactionBehavior<TestCommand, Result>),
            typeof(FlushOutgoingMessagesBehavior<TestCommand, Result>)
        ]);
    }

    [Fact(DisplayName = "CreateDefault registers the built-in request pipeline")]
    public void CreateDefaultShouldRegisterBuiltInRequestPipeline()
    {
        var configuration = WolverineRequestBehaviorConfiguration.CreateDefault();

        var registry = configuration.Build();

        registry.BeforeMiddlewareTypes.ShouldBe(
        [
            typeof(ValidationBehavior<,>),
            typeof(BeginTransactionBehavior<,>)
        ]);
        registry.AfterMiddlewareTypes.ShouldBe(
        [
            typeof(PublishDomainEventsBehavior<,>),
            typeof(PersistOutgoingMessagesBehavior<,>),
            typeof(CommitTransactionBehavior<,>),
            typeof(FlushOutgoingMessagesBehavior<,>)
        ]);
        registry.FinallyMiddlewareTypes.ShouldBe([typeof(CleanupTransactionBehavior<,>)]);
    }

    [Fact(DisplayName = "CreateModularDefault wraps the built-in pipeline with module activation")]
    public void CreateModularDefaultShouldWrapBuiltInPipelineWithModuleActivation()
    {
        var configuration = WolverineRequestBehaviorConfiguration.CreateModularDefault();

        var registry = configuration.Build();

        registry.BeforeMiddlewareTypes.ShouldBe(
        [
            typeof(ActivateWolverineModuleBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(BeginTransactionBehavior<,>)
        ]);
        registry.AfterMiddlewareTypes.ShouldBe(
        [
            typeof(PublishDomainEventsBehavior<,>),
            typeof(PersistOutgoingMessagesBehavior<,>),
            typeof(CommitTransactionBehavior<,>),
            typeof(FlushOutgoingMessagesBehavior<,>)
        ]);
        registry.FinallyMiddlewareTypes.ShouldBe(
        [
            typeof(CleanupTransactionBehavior<,>),
            typeof(DeactivateWolverineModuleBehavior<,>)
        ]);
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

        registry.BeforeMiddlewareTypes.ShouldBe(
        [
            typeof(ValidationBehavior<,>),
            typeof(BeginTransactionBehavior<,>),
            typeof(TestBeforeBehavior<,>)
        ]);
        registry.AfterMiddlewareTypes.ShouldBe(
        [
            typeof(PublishDomainEventsBehavior<,>),
            typeof(PersistOutgoingMessagesBehavior<,>),
            typeof(TestAfterBehavior<,>),
            typeof(CommitTransactionBehavior<,>),
            typeof(FlushOutgoingMessagesBehavior<,>)
        ]);
        registry.FinallyMiddlewareTypes.ShouldBe(
        [
            typeof(CleanupTransactionBehavior<,>),
            typeof(TestFinallyBehavior<,>)
        ]);
    }

    [Fact(DisplayName = "Stage configuration rejects missing anchor behavior")]
    public void StageConfigurationShouldRejectMissingAnchorBehavior()
    {
        var configuration = WolverineRequestBehaviorConfiguration.CreateDefault();

        void act()
        {
            configuration.Before.InsertBefore(
                typeof(TestBeforeBehavior<,>),
                typeof(SecondBeforeBehavior<,>));
        }

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

        registry.BeforeMiddlewareTypes.ShouldBe(
        [
            typeof(ValidationBehavior<,>),
            typeof(BeginTransactionBehavior<,>),
            typeof(SecondBeforeBehavior<TestCommand, Result>),
            typeof(ClosedCommandBeforeBehavior),
            typeof(TestBeforeBehavior<TestCommand, Result>)
        ]);
    }
}
