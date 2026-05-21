namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

/// <summary>
/// Configures the Kafka topic used to publish a specific event type.
/// </summary>
public class KafkaProducerOption
{
    /// <summary>
    /// Gets or sets the registered Kafka broker name. A null or empty value uses the primary broker.
    /// </summary>
    public string? BrokerName { get; set; }

    /// <summary>
    /// Gets or sets the Kafka topic name where the event is published.
    /// </summary>
    public string TopicName { get; set; } = string.Empty;
}
