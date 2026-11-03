using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database.Entities;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Fixtures;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Queries;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Support;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Events;

using Wolverine.Tracking;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests;

public sealed class NativeDomainEventPublicationTests(PostgreSqlContainerFixture fixture)
    : IClassFixture<PostgreSqlContainerFixture>
{
    [Theory(DisplayName = "Query pipelines leave tracked domain events untouched")]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task QueryPipelinesShouldNotPublishOrClearTrackedEvents(bool useModules, bool failValidation)
    {
        await using var app = await fixture.CreateApplicationAsync(useModuleRouting: useModules);
        await using var scope = app.Host.Services.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var dbContext = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        dbContext.Records.Add(new IntegrationRecord { Id = id, Name = "query" });
        await dbContext.SaveChangesAsync(ct);
        var tracker = scope.ServiceProvider.GetRequiredService<IAggregateTracker>();
        var aggregate = new TestEventAggregate(id);
        var domainEvent = new SharedModuleFollowUpEvent(id);
        aggregate.Raise(domainEvent);
        tracker.Track(aggregate);
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.QueryAsync(new GetIntegrationRecordQuery(failValidation ? Guid.Empty : id), ct);

        result.IsFailure.ShouldBe(failValidation);
        if (!failValidation)
        {
            result.Value.Id.ShouldBe(id);
        }

        aggregate.GetDomainEvents().ShouldHaveSingleItem().ShouldBeSameAs(domainEvent);
        tracker.GetAll().ShouldHaveSingleItem().ShouldBeSameAs(aggregate);
    }

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
