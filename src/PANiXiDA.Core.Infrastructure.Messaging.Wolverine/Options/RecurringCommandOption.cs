namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

/// <summary>
/// Configures a recurring application command from a named configuration section.
/// </summary>
public class RecurringCommandOption
{
    /// <summary>
    /// Gets or sets whether the schedule is registered when the host is built.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the stable schedule name, unique across the Wolverine application.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a five-field cron expression, or a six-field expression including seconds.
    /// </summary>
    public string CronExpression { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the system time zone identifier used to evaluate the cron expression. Defaults to UTC.
    /// </summary>
    public string TimeZoneId { get; set; } = "UTC";
}
