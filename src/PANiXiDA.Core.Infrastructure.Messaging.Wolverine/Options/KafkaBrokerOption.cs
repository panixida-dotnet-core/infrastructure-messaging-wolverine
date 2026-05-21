namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

/// <summary>
/// Configures a Kafka broker connection used by Wolverine.
/// </summary>
public class KafkaBrokerOption
{
    /// <summary>
    /// Gets or sets the registered Kafka broker name. A null or empty value configures the primary broker.
    /// </summary>
    public string? BrokerName { get; set; }

    /// <summary>
    /// Gets or sets the Kafka bootstrap servers connection string.
    /// </summary>
    public string BootstrapServers { get; set; } = string.Empty;
}
