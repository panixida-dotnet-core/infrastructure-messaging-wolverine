using Microsoft.Extensions.Options;

using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

internal sealed class RecurringCommandOptionValidator : IValidateOptions<RecurringCommandOption>
{
    public ValidateOptionsResult Validate(string? name, RecurringCommandOption options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var sectionName = name ?? nameof(RecurringCommandOption);
        List<string> failures = [];
        if (string.IsNullOrWhiteSpace(options.Name))
        {
            failures.Add($"{sectionName}:Name must not be empty.");
        }

        ValidateCronExpression(sectionName, options.CronExpression, failures);
        ValidateTimeZone(sectionName, options.TimeZoneId, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateCronExpression(string sectionName, string expression, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            failures.Add($"{sectionName}:CronExpression must not be empty.");
            return;
        }

        try
        {
            _ = new CronSchedule(expression);
        }
        catch (ArgumentException)
        {
            failures.Add($"{sectionName}:CronExpression must be a valid Wolverine cron expression with a minimum cadence of five seconds.");
        }
    }

    private static void ValidateTimeZone(string sectionName, string timeZoneId, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            failures.Add($"{sectionName}:TimeZoneId must not be empty.");
            return;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            failures.Add($"{sectionName}:TimeZoneId must identify a valid system time zone.");
        }
    }
}
