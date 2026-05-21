using Confluent.Kafka;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

/// <summary>
/// Configures the Kafka topic used to consume a specific event type.
/// </summary>
public class KafkaConsumerOption
{
    /// <summary>
    /// Gets or sets the registered Kafka broker name. A null or empty value uses the primary broker.
    /// </summary>
    public string? BrokerName { get; set; }

    /// <summary>
    /// Gets or sets the Kafka topic name where the event is consumed from.
    /// </summary>
    public string TopicName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Kafka consumer group id. A null or empty value keeps the Wolverine default.
    /// </summary>
    public string? ConsumerGroupId { get; set; }

    /// <summary>
    /// Gets or sets the offset reset rule for a new Kafka consumer group.
    /// </summary>
    public AutoOffsetReset? AutoOffsetReset { get; set; }
}
