using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Fixtures;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Events;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Handlers;

using Wolverine.Tracking;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests;

public sealed class MessageBehaviorPipelineTests(PostgreSqlContainerFixture fixture) : IClassFixture<PostgreSqlContainerFixture>
{
    private static readonly string[] expectedEventPipeline = ["event.before", "event.handler", "event.after", "event.finally"];

    [Theory(DisplayName = "Native static mappers preserve cascading messages through the shared behavior pipeline")]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task NativeMappersShouldPreserveCascadingMessages(bool useModules, int returnKind)
    {
        await using var app = await fixture.CreateApplicationAsync(
            configureRequestBehaviors: ConfigureBehaviors, useModuleRouting: useModules);
        var id = Guid.NewGuid();
        var tracked = await app.Host.Services.TrackActivity().Timeout(TimeSpan.FromSeconds(60))
            .ExecuteAndWaitAsync(context => returnKind switch
            {
                0 => context.PublishAsync(new CascadingEvent(id)).AsTask(),
                1 => context.PublishAsync(new AsyncCascadingEvent(id)).AsTask(),
                _ => context.PublishAsync(new ValueTaskCascadingEvent(id)).AsTask()
            });

        tracked.FindSingleTrackedMessageOfType<PipelineEvent>().EventId.ShouldBe(id);
        (await app.CountHandledEventsAsync(id)).ShouldBe(1);
        app.Journal.Entries.Count(entry => entry == "event.before").ShouldBe(2);
        app.Journal.Entries.Count(entry => entry == "event.finally").ShouldBe(2);
    }

    [Theory(DisplayName = "A rejected native mapper does not emit its cascading message")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeMapperShouldNotCascadeAfterRejectedBehavior(bool useModules)
    {
        await using var app = await fixture.CreateApplicationAsync(
            configureRequestBehaviors: ConfigureBehaviors, useModuleRouting: useModules);
        app.Journal.RejectEvents = true;
        var id = Guid.NewGuid();

        await app.Host.Services.TrackActivity().Timeout(TimeSpan.FromSeconds(60)).DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(context => context.PublishAsync(new CascadingEvent(id)).AsTask());
        await WolverineIntegrationApp.WaitUntilAsync(
            async () => await app.CountRowsAsync("wolverine.wolverine_dead_letters") == 1,
            TimeSpan.FromSeconds(60));

        (await app.CountHandledEventsAsync(id)).ShouldBe(0);
        app.Journal.Entries.ShouldNotContain("event.handler");
    }

    [Theory(DisplayName = "Registered message behaviors run around native event handlers without dedicated policies")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeEventsShouldUseRegisteredPipeline(bool useModules)
    {
        await using var app = await fixture.CreateApplicationAsync(
            configureRequestBehaviors: ConfigureBehaviors, useModuleRouting: useModules);
        var id = Guid.NewGuid();

        await app.Host.Services.TrackActivity().Timeout(TimeSpan.FromSeconds(60))
            .ExecuteAndWaitAsync(context => context.PublishAsync(new PipelineEvent(id)).AsTask());

        app.Journal.Entries.Where(entry => entry.StartsWith("event.", StringComparison.Ordinal))
            .ShouldBe(expectedEventPipeline);
        app.Journal.AfterResult.ShouldNotBeNull().IsSuccess.ShouldBeTrue();
        app.Journal.FinallyResult.ShouldBeSameAs(app.Journal.AfterResult);
        app.Journal.FinallyException.ShouldBeNull();
        app.Journal.MessageHandlerType.ShouldBe(typeof(PipelineEventHandler));
        (await app.CountHandledEventsAsync(id)).ShouldBe(1);
    }

    [Theory(DisplayName = "Native message pipeline failures roll back persistence and reach Wolverine failure handling")]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, true)]
    public async Task NativePipelineFailuresShouldRollback(bool useModules, bool rejectBefore, bool failAfter, bool throwHandler)
    {
        await using var app = await fixture.CreateApplicationAsync(
            configureRequestBehaviors: ConfigureBehaviors, useModuleRouting: useModules);
        app.Journal.RejectEvents = rejectBefore;
        app.Journal.FailEventAfter = failAfter;
        var id = Guid.NewGuid();

        await app.Host.Services.TrackActivity().Timeout(TimeSpan.FromSeconds(60)).DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(context => context.PublishAsync(new PipelineEvent(id, throwHandler)).AsTask());
        await WolverineIntegrationApp.WaitUntilAsync(
            async () => await app.CountRowsAsync("wolverine.wolverine_dead_letters") == 1,
            TimeSpan.FromSeconds(60));

        (await app.CountHandledEventsAsync(id)).ShouldBe(0);
        app.Journal.Entries.ShouldContain("event.finally");
        if (rejectBefore)
        {
            app.Journal.Entries.ShouldNotContain("event.handler");
            app.Journal.AfterResult.ShouldNotBeNull().IsFailure.ShouldBeTrue();
        }
        else
        {
            app.Journal.FinallyException.ShouldBeOfType<InvalidOperationException>();
        }
    }

    private static void ConfigureBehaviors(WolverineRequestBehaviorConfiguration configuration)
    {
        configuration.Before.Add(typeof(EventPipelineBefore<>));
        configuration.After.Add(typeof(EventPipelineAfter<,,>));
        configuration.Finally.Add(typeof(EventPipelineFinally<,>));
    }
}
