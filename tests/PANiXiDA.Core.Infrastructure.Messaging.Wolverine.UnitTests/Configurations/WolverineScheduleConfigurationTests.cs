using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Configurations;

using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class WolverineScheduleConfigurationTests
{
    [Fact(DisplayName = "Recurring commands use typed settings without executing their factory during registration")]
    public void RecurringCommandsShouldUseTypedSettings()
    {
        var options = new WolverineOptions();
        var configuration = CreateConfiguration();
        configuration["TestRecurringCommandOption:TimeZoneId"] = "Europe/Moscow";
        var schedules = new WolverineScheduleConfiguration(options, configuration);

        var result = schedules.AddRecurringCommand<TestRecurringCommandOption, TestCommand>(_ =>
            throw new InvalidOperationException("A factory must not execute during registration."));

        result.ShouldBeSameAs(schedules);
        options.Durability.EnableRecurringMessages.ShouldBeTrue();
        var schedule = options.Schedules.FindByName("test-command");
        schedule.ShouldNotBeNull();
        schedule.MessageType.ShouldBe(typeof(TestCommand));
        schedule.Schedule.Expression.ShouldBe("0 3 * * *");
        schedule.Schedule.TimeZone.Id.ShouldBe("Europe/Moscow");
    }

    [Fact(DisplayName = "Recurring commands default to UTC")]
    public void RecurringCommandsShouldDefaultToUtc()
    {
        var options = new WolverineOptions();
        var schedules = new WolverineScheduleConfiguration(options, CreateConfiguration());

        schedules.AddRecurringCommand<TestRecurringCommandOption, TestCommand>(_ => new TestCommand(Guid.NewGuid()));

        options.Schedules.FindByName("test-command")!.Schedule.TimeZone.ShouldBe(TimeZoneInfo.Utc);
    }

    [Fact(DisplayName = "Disabled recurring commands do not require schedule values or enable recurring infrastructure")]
    public void DisabledCommandsShouldNotEnableSchedules()
    {
        var options = new WolverineOptions();
        var configuration = new ConfigurationManager
        {
            ["TestRecurringCommandOption:Enabled"] = "false",
        };
        var schedules = new WolverineScheduleConfiguration(options, configuration);

        var result = schedules.AddRecurringCommand<TestRecurringCommandOption, TestCommand>(_ =>
            throw new InvalidOperationException("A disabled factory must not execute."));

        result.ShouldBeSameAs(schedules);
        options.Durability.EnableRecurringMessages.ShouldBeFalse();
        options.Schedules.FindByName("test-command").ShouldBeNull();
    }

    [Fact(DisplayName = "Recurring commands reject a null command factory")]
    public void RecurringCommandsShouldRejectNullFactory()
    {
        var schedules = new WolverineScheduleConfiguration(new WolverineOptions(), CreateConfiguration());

        var exception = Should.Throw<ArgumentNullException>(() =>
            schedules.AddRecurringCommand<TestRecurringCommandOption, TestCommand>(null!));

        exception.ParamName.ShouldBe("createCommand");
    }

    [Fact(DisplayName = "Recurring commands reject a missing typed section")]
    public void RecurringCommandsShouldRejectMissingSection()
    {
        var schedules = new WolverineScheduleConfiguration(new WolverineOptions(), new ConfigurationManager());

        var exception = Should.Throw<InvalidOperationException>(() =>
            schedules.AddRecurringCommand<TestRecurringCommandOption, TestCommand>(_ => new TestCommand(Guid.NewGuid())));

        exception.Message.ShouldBe("Configuration section 'TestRecurringCommandOption' was not found.");
    }

    [Fact(DisplayName = "Recurring commands reject an option section that cannot be bound")]
    public void RecurringCommandsShouldRejectUnboundSection()
    {
        var configuration = new ConfigurationManager { ["TestRecurringCommandOption"] = string.Empty };
        var schedules = new WolverineScheduleConfiguration(new WolverineOptions(), configuration);

        var exception = Should.Throw<InvalidOperationException>(() =>
            schedules.AddRecurringCommand<TestRecurringCommandOption, TestCommand>(_ => new TestCommand(Guid.NewGuid())));

        exception.Message.ShouldBe(
            $"Configuration section 'TestRecurringCommandOption' could not be bound to '{typeof(TestRecurringCommandOption).FullName}'.");
    }

    [Theory(DisplayName = "Recurring commands reject blank required settings")]
    [InlineData("Name")]
    [InlineData("CronExpression")]
    [InlineData("TimeZoneId")]
    public void RecurringCommandsShouldRejectBlankSettings(string setting)
    {
        var configuration = CreateConfiguration();
        configuration[$"TestRecurringCommandOption:{setting}"] = " ";
        var options = new WolverineOptions();
        var schedules = new WolverineScheduleConfiguration(options, configuration);

        var exception = Should.Throw<ArgumentException>(() =>
            schedules.AddRecurringCommand<TestRecurringCommandOption, TestCommand>(_ => new TestCommand(Guid.NewGuid())));

        exception.ParamName.ShouldBe($"option.{setting}");
        options.Durability.EnableRecurringMessages.ShouldBeFalse();
    }

    [Theory(DisplayName = "Recurring commands reject invalid or unsupported cron expressions")]
    [InlineData("not a cron")]
    [InlineData("* * * * * *")]
    public void RecurringCommandsShouldRejectInvalidCron(string cron)
    {
        var configuration = CreateConfiguration();
        configuration["TestRecurringCommandOption:CronExpression"] = cron;
        var schedules = new WolverineScheduleConfiguration(new WolverineOptions(), configuration);

        Should.Throw<ArgumentException>(() =>
            schedules.AddRecurringCommand<TestRecurringCommandOption, TestCommand>(_ => new TestCommand(Guid.NewGuid())));
    }

    [Fact(DisplayName = "Recurring commands reject unknown time zones")]
    public void RecurringCommandsShouldRejectUnknownTimeZone()
    {
        var configuration = CreateConfiguration();
        configuration["TestRecurringCommandOption:TimeZoneId"] = "Unknown/TestTimeZone";
        var schedules = new WolverineScheduleConfiguration(new WolverineOptions(), configuration);

        Should.Throw<TimeZoneNotFoundException>(() =>
            schedules.AddRecurringCommand<TestRecurringCommandOption, TestCommand>(_ => new TestCommand(Guid.NewGuid())));
    }

    [Fact(DisplayName = "Recurring commands reject duplicate schedule names")]
    public void RecurringCommandsShouldRejectDuplicateNames()
    {
        var schedules = new WolverineScheduleConfiguration(new WolverineOptions(), CreateConfiguration());
        schedules.AddRecurringCommand<TestRecurringCommandOption, TestCommand>(_ => new TestCommand(Guid.NewGuid()));

        Should.Throw<ArgumentException>(() =>
            schedules.AddRecurringCommand<TestRecurringCommandOption, TestCommand>(_ => new TestCommand(Guid.NewGuid())));
    }

    internal static ConfigurationManager CreateConfiguration()
    {
        return new ConfigurationManager
        {
            ["TestRecurringCommandOption:Name"] = "test-command",
            ["TestRecurringCommandOption:CronExpression"] = "0 3 * * *",
        };
    }
}
