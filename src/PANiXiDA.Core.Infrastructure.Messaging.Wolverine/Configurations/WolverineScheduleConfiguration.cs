using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;

/// <summary>
/// Registers recurring application commands from typed configuration sections.
/// </summary>
/// <param name="wolverineOptions">The Wolverine options receiving the recurring command registrations.</param>
/// <param name="configuration">The application configuration containing typed schedule options.</param>
public sealed class WolverineScheduleConfiguration(WolverineOptions wolverineOptions, IConfiguration configuration)
{
    /// <summary>
    /// Registers a parameterless command from the child section named after the command type.
    /// A new command is created for each occurrence.
    /// </summary>
    /// <typeparam name="TCommand">The application command with a public parameterless constructor.</typeparam>
    /// <param name="parentSectionName">The parent configuration section. Defaults to Messaging:Schedules.</param>
    /// <returns>The same configuration instance for registering additional schedules.</returns>
    /// <exception cref="InvalidOperationException">The configuration section is missing or cannot be bound.</exception>
    /// <exception cref="OptionsValidationException">An enabled schedule has invalid settings.</exception>
    /// <exception cref="ArgumentException">The schedule name is already registered.</exception>
    public WolverineScheduleConfiguration AddRecurringCommand<TCommand>(
        string parentSectionName = "Messaging:Schedules")
        where TCommand : class, ICommand<Result>, new()
    {
        return AddRecurringCommand(_ => new TCommand(), parentSectionName);
    }

    /// <summary>
    /// Registers a command factory from the child section named after the command type.
    /// </summary>
    /// <typeparam name="TCommand">The application command to dispatch on each occurrence.</typeparam>
    /// <param name="createCommand">Builds a command from its scheduled occurrence time, not its delivery time.</param>
    /// <param name="parentSectionName">The parent configuration section. Defaults to Messaging:Schedules.</param>
    /// <returns>The same configuration instance for registering additional schedules.</returns>
    /// <exception cref="InvalidOperationException">The configuration section is missing or cannot be bound.</exception>
    /// <exception cref="OptionsValidationException">An enabled schedule has invalid settings.</exception>
    /// <exception cref="ArgumentException">The schedule name is already registered.</exception>
    public WolverineScheduleConfiguration AddRecurringCommand<TCommand>(
        Func<DateTimeOffset, TCommand> createCommand,
        string parentSectionName = "Messaging:Schedules")
        where TCommand : class, ICommand<Result>
    {
        return RegisterRecurringCommand<RecurringCommandOption, TCommand>(
            $"{parentSectionName}:{typeof(TCommand).Name}", createCommand);
    }

    /// <summary>
    /// Registers a recurring command from the section named after <typeparamref name="TOption"/>.
    /// Disabled options do not register a schedule or validate its name, cron expression, or time zone.
    /// </summary>
    /// <typeparam name="TOption">The configuration model derived from <see cref="RecurringCommandOption"/>.</typeparam>
    /// <typeparam name="TCommand">The application command to dispatch on each occurrence.</typeparam>
    /// <param name="createCommand">Builds a command from its scheduled occurrence time, not its delivery time.</param>
    /// <returns>The same configuration instance for registering additional schedules.</returns>
    /// <exception cref="InvalidOperationException">The configuration section is missing or cannot be bound.</exception>
    /// <exception cref="OptionsValidationException">An enabled schedule has invalid settings.</exception>
    /// <exception cref="ArgumentException">The schedule name is already registered.</exception>
    public WolverineScheduleConfiguration AddRecurringCommand<TOption, TCommand>(
        Func<DateTimeOffset, TCommand> createCommand)
        where TOption : RecurringCommandOption, new()
        where TCommand : class, ICommand<Result>
    {
        return RegisterRecurringCommand<TOption, TCommand>(typeof(TOption).Name, createCommand);
    }

    private WolverineScheduleConfiguration RegisterRecurringCommand<TOption, TCommand>(
        string sectionName,
        Func<DateTimeOffset, TCommand> createCommand)
        where TOption : RecurringCommandOption, new()
        where TCommand : class, ICommand<Result>
    {
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

        var validation = new RecurringCommandOptionValidator().Validate(sectionName, option);
        if (validation.Failed)
        {
            throw new OptionsValidationException(sectionName, typeof(TOption), validation.Failures);
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(option.TimeZoneId);
        var schedule = new CronSchedule(option.CronExpression, timeZone);

        wolverineOptions.Schedules.ScheduleRecurring(option.Name, schedule, createCommand);

        return this;
    }
}
