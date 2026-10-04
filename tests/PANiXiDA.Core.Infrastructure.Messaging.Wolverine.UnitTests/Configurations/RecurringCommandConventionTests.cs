using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;

using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class RecurringCommandConventionTests
{
    private const string ScheduleName = "convention-command";

    [Fact(DisplayName = "Parameterless commands register from the section named after the command type")]
    public void ParameterlessCommandsShouldUseConventionalSettings()
    {
        var options = new WolverineOptions();
        var configuration = CreateConfiguration<TestRecurringCommand>("Messaging:Schedules");
        var schedules = new WolverineScheduleConfiguration(options, configuration);

        var result = schedules.AddRecurringCommand<TestRecurringCommand>();

        result.ShouldBeSameAs(schedules);
        var schedule = options.Schedules.FindByName(ScheduleName)!;
        schedule.MessageType.ShouldBe(typeof(TestRecurringCommand));
        schedule.Schedule.Expression.ShouldBe("0 3 * * *");
        schedule.Schedule.TimeZone.ShouldBe(TimeZoneInfo.Utc);
    }

    [Theory(DisplayName = "Command factories bind conventional or custom sections without executing during registration")]
    [InlineData("Messaging:Schedules")]
    [InlineData("Maintenance:Schedules")]
    public void FactoriesShouldBindWithoutExecuting(string parentSection)
    {
        var options = new WolverineOptions();
        var configuration = CreateConfiguration<TestCommand>(parentSection);
        var schedules = new WolverineScheduleConfiguration(options, configuration);

        schedules.AddRecurringCommand<TestCommand>(
            _ => throw new InvalidOperationException("A factory must not execute during registration."), parentSection);

        options.Schedules.FindByName(ScheduleName)!.MessageType.ShouldBe(typeof(TestCommand));
    }

    [Fact(DisplayName = "Disabled conventional schedules require no other values")]
    public void DisabledCommandsShouldSkipRegistration()
    {
        var options = new WolverineOptions();
        var configuration = new ConfigurationManager
        {
            ["Messaging:Schedules:TestRecurringCommand:Enabled"] = "false",
        };

        new WolverineScheduleConfiguration(options, configuration).AddRecurringCommand<TestRecurringCommand>();

        options.Durability.EnableRecurringMessages.ShouldBeFalse();
    }

    [Fact(DisplayName = "Conventional schedules report missing command configuration")]
    public void MissingSectionsShouldFail()
    {
        var schedules = new WolverineScheduleConfiguration(new WolverineOptions(), new ConfigurationManager());

        var exception = Should.Throw<InvalidOperationException>(() => schedules.AddRecurringCommand<TestRecurringCommand>());

        exception.Message.ShouldBe("Configuration section 'Messaging:Schedules:TestRecurringCommand' was not found.");
    }

    [Fact(DisplayName = "Conventional schedules report all invalid settings with their configuration paths")]
    public void InvalidOptionsShouldReportAllFailures()
    {
        var options = new WolverineOptions();
        var configuration = new ConfigurationManager
        {
            ["Messaging:Schedules:TestRecurringCommand:Enabled"] = "true",
            ["Messaging:Schedules:TestRecurringCommand:TimeZoneId"] = "Unknown/TestTimeZone",
        };
        var schedules = new WolverineScheduleConfiguration(options, configuration);

        var exception = Should.Throw<OptionsValidationException>(() => schedules.AddRecurringCommand<TestRecurringCommand>());

        exception.OptionsName.ShouldBe("Messaging:Schedules:TestRecurringCommand");
        exception.Failures.Count().ShouldBe(3);
        exception.Failures.ShouldAllBe(failure => failure.StartsWith("Messaging:Schedules:TestRecurringCommand:", StringComparison.Ordinal));
        options.Durability.EnableRecurringMessages.ShouldBeFalse();
    }

    private static ConfigurationManager CreateConfiguration<TCommand>(string parentSection)
    {
        return new ConfigurationManager
        {
            [$"{parentSection}:{typeof(TCommand).Name}:Name"] = ScheduleName,
            [$"{parentSection}:{typeof(TCommand).Name}:CronExpression"] = "0 3 * * *",
        };
    }
}
