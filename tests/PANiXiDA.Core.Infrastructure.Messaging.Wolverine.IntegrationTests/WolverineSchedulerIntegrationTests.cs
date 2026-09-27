using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Application.Messaging.Scheduling;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Fixtures;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Commands;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Support;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Wolverine;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Events;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Handlers;

using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests;

public sealed class WolverineSchedulerIntegrationTests(PostgreSqlContainerFixture fixture)
    : IClassFixture<PostgreSqlContainerFixture>
{
    [Theory(DisplayName = "Scheduled commands and events persist across application restart")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScheduledMessagesShouldSurviveRestart(bool useModules)
    {
        var command = new ScheduleIntegrationMessagesCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(30));
        await using (var app = await fixture.CreateApplicationAsync(useModuleRouting: useModules))
        {
            command = command with { DeliverAt = DateTimeOffset.UtcNow.AddSeconds(15) };

            var result = await app.ExecuteWithMediatorAsync(
                (mediator, token) => mediator.SendAsync(command, token), TestContext.Current.CancellationToken);

            result.IsSuccess.ShouldBeTrue();
            (await app.CountRecordsAsync(command.RecordId)).ShouldBe(1);
            (await app.CountRecordsAsync(command.ScheduledRecordId)).ShouldBe(0);
            (await app.CountHandledEventsAsync(command.EventId)).ShouldBe(0);
            (await CountEnvelopesAsync(app)).ShouldBe(2);
        }

        await using var restarted = await fixture.CreateApplicationAsync(
            useModuleRouting: useModules, resetDatabase: false);

        await WolverineIntegrationApp.WaitUntilAsync(
            async () => await restarted.CountRecordsAsync(command.ScheduledRecordId) == 1 &&
                await restarted.CountHandledEventsAsync(command.EventId) == 1,
            TimeSpan.FromSeconds(60));
    }

    [Theory(DisplayName = "Scheduled messages roll back with failed command transactions")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ScheduledMessagesShouldRollBack(bool useModules, bool throwException)
    {
        await using var app = await fixture.CreateApplicationAsync(useModuleRouting: useModules);
        var command = new ScheduleIntegrationMessagesCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(15),
            ThrowAfterScheduling: throwException, ReturnFailure: !throwException);
        Task<Result> act() => app.ExecuteWithMediatorAsync(
            (mediator, token) => mediator.SendAsync(command, token), TestContext.Current.CancellationToken);

        if (throwException)
        {
            var exception = await Should.ThrowAsync<Exception>(act);
            (exception is PlannedCommandException || exception.InnerException is PlannedCommandException)
                .ShouldBeTrue();
        }
        else
        {
            (await act()).IsFailure.ShouldBeTrue();
        }

        (await app.CountRecordsAsync(command.RecordId)).ShouldBe(0);
        (await app.CountRecordsAsync(command.ScheduledRecordId)).ShouldBe(0);
        (await app.CountHandledEventsAsync(command.EventId)).ShouldBe(0);
        (await CountEnvelopesAsync(app)).ShouldBe(0);
    }

    [Fact(DisplayName = "Keyed module scheduler delivers delayed events to all module subscribers")]
    public async Task KeyedSchedulerShouldFanOutEvents()
    {
        await using var app = await fixture.CreateApplicationAsync(useModuleRouting: true);
        var id = Guid.NewGuid();
        await using (var scope = app.Host.Services.CreateAsyncScope())
        {
            var scheduler = scope.ServiceProvider.GetRequiredKeyedService<IScheduler>(typeof(SecondModuleDbContext));
            var outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox<SecondModuleDbContext>>();

            await scheduler.ScheduleAsync(
                new SharedModuleEvent(id, "scheduled fan-out", FailSecondModuleHandler: false),
                TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            await outbox.SaveChangesAndFlushMessagesAsync(TestContext.Current.CancellationToken);
        }

        (await app.CountHandledEventsAsync(id)).ShouldBe(0);
        (await app.CountSecondModuleEventsAsync(id, nameof(SharedModuleEvent))).ShouldBe(0);
        await WolverineIntegrationApp.WaitUntilAsync(
            async () => await app.CountHandledEventsAsync(id) == 1 &&
                await app.CountSecondModuleEventsAsync(id, nameof(SharedModuleEvent)) == 1,
            TimeSpan.FromSeconds(30));
    }

    [Theory(DisplayName = "Native handlers schedule messages in their own module transaction")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeHandlerShouldUseModuleTransaction(bool fail)
    {
        await using var app = await fixture.CreateApplicationAsync(useModuleRouting: true);
        await using var scope = app.Host.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var @event = new ScheduleSecondModuleEvent(Guid.NewGuid(), fail);

        if (fail)
        {
            var exception = await Should.ThrowAsync<Exception>(() =>
                bus.InvokeAsync(@event, TestContext.Current.CancellationToken));
            (exception is PlannedSecondModuleException || exception.InnerException is PlannedSecondModuleException)
                .ShouldBeTrue();
        }
        else
        {
            await bus.InvokeAsync(@event, TestContext.Current.CancellationToken);
        }

        (await app.CountSecondModuleRecordsAsync(@event.EventId)).ShouldBe(fail ? 0 : 1);
        (await app.CountRecordsAsync(@event.EventId)).ShouldBe(0);
        (await CountEnvelopesAsync(app)).ShouldBe(fail ? 0 : 2);
    }

    private static async Task<long> CountEnvelopesAsync(WolverineIntegrationApp app)
    {
        return await app.CountRowsAsync(WolverineStorageConstants.IncomingEnvelopesTable) +
            await app.CountRowsAsync(WolverineStorageConstants.OutgoingEnvelopesTable);
    }
}
