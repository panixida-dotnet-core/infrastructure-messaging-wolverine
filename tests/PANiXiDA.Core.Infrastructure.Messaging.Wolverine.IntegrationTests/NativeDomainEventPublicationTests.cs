using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Fixtures;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Events;

using Wolverine.Tracking;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests;

public sealed class NativeDomainEventPublicationTests(PostgreSqlContainerFixture fixture)
    : IClassFixture<PostgreSqlContainerFixture>
{
    [Fact(DisplayName = "Native event handlers publish tracked aggregate events exactly once")]
    public async Task NativeEventsShouldPublishTrackedAggregateEventsOnce()
    {
        await using var app = await fixture.CreateApplicationAsync(useModuleRouting: true);
        var id = Guid.NewGuid();
        var message = new SharedModuleEvent(id, "tracked-event", FailSecondModuleHandler: false);

        var tracked = await app.Host.Services.TrackActivity().Timeout(TimeSpan.FromSeconds(60))
            .ExecuteAndWaitAsync(context => context.PublishAsync(message).AsTask());

        tracked.FindSingleTrackedMessageOfType<SharedModuleFollowUpEvent>().EventId.ShouldBe(id);
        (await app.CountHandledEventsAsync(id)).ShouldBe(1);
        (await app.CountSecondModuleEventsAsync(id, nameof(SharedModuleEvent))).ShouldBe(1);
        (await app.CountSecondModuleEventsAsync(id, nameof(SharedModuleFollowUpEvent))).ShouldBe(1);
    }

    [Theory(DisplayName = "Native event failures roll back changes and pending aggregate events")]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task NativeEventFailuresShouldRollbackChangesAndOutgoingEvents(bool failHandler, bool failSave)
    {
        await using var app = await fixture.CreateApplicationAsync(useModuleRouting: true);
        var id = Guid.NewGuid();
        var message = new SharedModuleEvent(
            id,
            "failed-event",
            FailSecondModuleHandler: false,
            FailFirstModuleHandler: failHandler,
            FailFirstModuleSave: failSave);

        await app.Host.Services.TrackActivity().Timeout(TimeSpan.FromSeconds(60))
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(context => context.PublishAsync(message).AsTask());
        await WolverineIntegrationApp.WaitUntilAsync(
            async () => await app.CountRowsAsync("wolverine.wolverine_dead_letters") == 1 &&
                await app.CountSecondModuleEventsAsync(id, nameof(SharedModuleEvent)) == 1,
            TimeSpan.FromSeconds(60));

        (await app.CountHandledEventsAsync(id)).ShouldBe(0);
        (await app.CountSecondModuleEventsAsync(id, nameof(SharedModuleFollowUpEvent))).ShouldBe(0);
    }
}
