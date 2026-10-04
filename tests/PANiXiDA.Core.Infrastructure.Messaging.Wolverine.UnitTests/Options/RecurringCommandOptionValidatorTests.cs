using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Options;

public sealed class RecurringCommandOptionValidatorTests
{
    [Fact(DisplayName = "Disabled recurring options pass validation without schedule settings")]
    public void DisabledOptionsShouldPassValidation()
    {
        var options = new RecurringCommandOption { Enabled = false, TimeZoneId = "unknown" };

        var result = new RecurringCommandOptionValidator().Validate(null, options);

        result.Succeeded.ShouldBeTrue();
    }

    [Theory(DisplayName = "Recurring options support five-field and six-field cron expressions")]
    [InlineData("0 3 * * *")]
    [InlineData("*/10 * * * * *")]
    public void ValidOptionsShouldPassValidation(string cron)
    {
        var options = new RecurringCommandOption { Name = "cleanup", CronExpression = cron };

        var result = new RecurringCommandOptionValidator().Validate(null, options);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact(DisplayName = "Unnamed recurring options use the model name in validation errors")]
    public void UnnamedOptionsShouldReportModelName()
    {
        var options = new RecurringCommandOption { CronExpression = "0 3 * * *" };

        var result = new RecurringCommandOptionValidator().Validate(null, options);

        result.Failures.ShouldBe(["RecurringCommandOption:Name must not be empty."]);
    }
}
