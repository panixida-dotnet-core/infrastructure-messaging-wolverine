using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Fixtures;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Commands;

using Wolverine.Persistence.Durability;
using Wolverine.Runtime.Recurring;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests;

public sealed class WolverineRecurringCommandIntegrationTests(PostgreSqlContainerFixture fixture)
    : IClassFixture<PostgreSqlContainerFixture>
{
    [Fact(DisplayName = "Parameterless recurring registration creates distinct commands and persists their outbox events")]
    public async Task ParameterlessCommandsShouldBeCreatedPerOccurrence()
    {
        var configuration = CreateConfiguration("*/10 * * * * *");
        await using var app = await fixture.CreateApplicationAsync(
            configuration: configuration,
            configureSchedules: schedules => schedules.AddRecurringCommand<CreateIntegrationRecordAndPublishEventCommand>());

        await WaitForOccurrencesAsync(app, 2);

        using var scope = app.Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        var records = await dbContext.Records.ToListAsync(TestContext.Current.CancellationToken);
        records.Count.ShouldBeGreaterThanOrEqualTo(2);
        records.Select(record => record.Id).Distinct().Count().ShouldBe(records.Count);
        records.ShouldAllBe(record => record.Name == "parameterless-command");
    }

    [Theory(DisplayName = "Recurring commands persist business changes and outbox events across host restarts")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecurringCommandsShouldExecuteAndContinueAfterRestart(bool useModules)
    {
        var configuration = CreateConfiguration("*/10 * * * * *");
        long beforeRestart;
        await using (var app = await fixture.CreateApplicationAsync(
            configuration: configuration, useModuleRouting: useModules, configureSchedules: RegisterSchedule))
        {
            await WaitForOccurrencesAsync(app, 2);
            var control = app.Host.Services.GetRequiredService<IRecurringScheduleControl>();
            var schedules = await control.QueryAsync(TestContext.Current.CancellationToken);
            schedules.Count.ShouldBe(1);
            beforeRestart = await app.CountRowsAsync("first_module.integration_records");
        }

        await using var restarted = await fixture.CreateApplicationAsync(
            configuration: configuration, useModuleRouting: useModules, resetDatabase: false,
            configureSchedules: RegisterSchedule);

        await WaitForOccurrencesAsync(restarted, beforeRestart + 2);
        using var scope = restarted.Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        var records = await dbContext.Records.ToListAsync(TestContext.Current.CancellationToken);

        records.Select(record => record.Name).Distinct().Count().ShouldBe(records.Count);
        foreach (var record in records)
        {
            var occurrence = DateTimeOffset.Parse(record.Name, CultureInfo.InvariantCulture);
            occurrence.Offset.ShouldBe(TimeSpan.Zero);
            (occurrence.Second % 10).ShouldBe(0);
            await WolverineIntegrationApp.WaitUntilAsync(
                async () => await restarted.CountHandledEventsAsync(record.Id) == 1,
                TimeSpan.FromSeconds(30));
        }
    }

    [Theory(DisplayName = "A pre-scheduled recurring command survives stopping and rebuilding the host")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingOccurrenceShouldSurviveRestart(bool useModules)
    {
        var configuration = CreateConfiguration("0 0 1 1 *");
        RecurringMessageRecord pending;
        await using (var app = await fixture.CreateApplicationAsync(
            configuration: configuration, useModuleRouting: useModules, configureSchedules: RegisterSchedule))
        {
            await WaitForPendingOccurrenceAsync(app);
            pending = (await app.Host.Services.GetRequiredService<IRecurringScheduleControl>()
                .QueryAsync(TestContext.Current.CancellationToken)).Single();
        }

        await using var restarted = await fixture.CreateApplicationAsync(
            configuration: configuration, useModuleRouting: useModules, resetDatabase: false,
            configureSchedules: RegisterSchedule);

        await WaitForPendingOccurrenceAsync(restarted);
        var restored = (await restarted.Host.Services.GetRequiredService<IRecurringScheduleControl>()
            .QueryAsync(TestContext.Current.CancellationToken)).Single();
        restored.EnvelopeIds.ShouldBe(pending.EnvelopeIds);
        restored.NextOccurrence.ShouldBe(pending.NextOccurrence);
        restored.DeduplicationId.ShouldBe(pending.DeduplicationId);
        (await restarted.CountRowsAsync("wolverine.wolverine_incoming_envelopes")).ShouldBe(1);
        (await restarted.CountRowsAsync("first_module.integration_records")).ShouldBe(0);
    }

    private static Task WaitForOccurrencesAsync(WolverineIntegrationApp app, long minimum)
    {
        return WolverineIntegrationApp.WaitUntilAsync(
            async () => await app.CountRowsAsync("first_module.integration_records") >= minimum &&
                await app.CountRowsAsync("first_module.handled_events") >= minimum,
            TimeSpan.FromSeconds(90));
    }

    private static Task WaitForPendingOccurrenceAsync(WolverineIntegrationApp app)
    {
        var control = app.Host.Services.GetRequiredService<IRecurringScheduleControl>();
        return WolverineIntegrationApp.WaitUntilAsync(async () =>
            (await control.QueryAsync(TestContext.Current.CancellationToken)).Count == 1 &&
                await app.CountRowsAsync("wolverine.wolverine_incoming_envelopes") == 1,
            TimeSpan.FromSeconds(60));
    }

    private static void RegisterSchedule(WolverineScheduleConfiguration schedules)
    {
        schedules.AddRecurringCommand<CreateIntegrationRecordAndPublishEventCommand>(
            occurrence => new CreateIntegrationRecordAndPublishEventCommand(
                Guid.NewGuid(), occurrence.ToString("O", CultureInfo.InvariantCulture)));
    }

    private static ConfigurationManager CreateConfiguration(string cron)
    {
        return new ConfigurationManager
        {
            ["Messaging:Schedules:CreateIntegrationRecordAndPublishEventCommand:Name"] = "integration-recurring-command",
            ["Messaging:Schedules:CreateIntegrationRecordAndPublishEventCommand:CronExpression"] = cron,
        };
    }
}
