using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;

/// <summary>
/// Registers recurring application commands from typed configuration sections.
/// </summary>
public sealed class WolverineScheduleConfiguration
{
    private readonly WolverineOptions wolverineOptions;
    private readonly IConfiguration configuration;

    internal WolverineScheduleConfiguration(WolverineOptions wolverineOptions, IConfiguration configuration)
    {
        this.wolverineOptions = wolverineOptions;
        this.configuration = configuration;
    }

    /// <summary>
    /// Registers a recurring command from the section named after <typeparamref name="TOption"/>.
    /// Disabled options do not register a schedule or validate its name, cron expression, or time zone.
    /// </summary>
    /// <typeparam name="TOption">The configuration model derived from <see cref="RecurringCommandOption"/>.</typeparam>
    /// <typeparam name="TCommand">The application command to dispatch on each occurrence.</typeparam>
    /// <param name="createCommand">Builds a command from its scheduled occurrence time, not its delivery time.</param>
    /// <returns>The same configuration instance for registering additional schedules.</returns>
    /// <exception cref="ArgumentNullException">The command factory is null.</exception>
    /// <exception cref="InvalidOperationException">The configuration section is missing or cannot be bound.</exception>
    /// <exception cref="ArgumentException">An enabled schedule has an invalid name, cron expression, or duplicate name.</exception>
    /// <exception cref="TimeZoneNotFoundException">The configured time zone is not available on the host.</exception>
    /// <exception cref="InvalidTimeZoneException">The configured time zone data is invalid.</exception>
    public WolverineScheduleConfiguration AddRecurringCommand<TOption, TCommand>(
        Func<DateTimeOffset, TCommand> createCommand)
        where TOption : RecurringCommandOption, new()
        where TCommand : class, ICommand<Result>
    {
        ArgumentNullException.ThrowIfNull(createCommand);

        var sectionName = typeof(TOption).Name;
        var section = configuration.GetSection(sectionName);
        if (!section.Exists())
        {
            throw new InvalidOperationException($"Configuration section '{sectionName}' was not found.");
        }

        var option = section.Get<TOption>()
            ?? throw new InvalidOperationException(
                $"Configuration section '{sectionName}' could not be bound to '{typeof(TOption).FullName}'.");

        if (!option.Enabled)
        {
            return this;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(option.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(option.CronExpression);
        ArgumentException.ThrowIfNullOrWhiteSpace(option.TimeZoneId);

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(option.TimeZoneId);
        var schedule = new CronSchedule(option.CronExpression, timeZone);

        wolverineOptions.Schedules.ScheduleRecurring(option.Name, schedule, createCommand);

        return this;
    }
}
