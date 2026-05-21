using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

using Wolverine;
using Wolverine.Kafka;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;

/// <summary>
/// Builds Wolverine Kafka broker, producer, and consumer topology from typed configuration sections.
/// </summary>
public sealed class WolverineKafkaConfiguration
{
    private readonly WolverineOptions wolverineOptions;
    private readonly IConfiguration configuration;

    internal WolverineKafkaConfiguration(
        WolverineOptions wolverineOptions,
        IConfiguration configuration)
    {
        this.wolverineOptions = wolverineOptions;
        this.configuration = configuration;
    }

    /// <summary>
    /// Registers a Kafka broker from the section named after <typeparamref name="TOption"/>.
    /// </summary>
    /// <typeparam name="TOption">The configuration model type inherited from <see cref="KafkaBrokerOption"/>.</typeparam>
    /// <returns>The Wolverine Kafka transport expression for additional configuration.</returns>
    public KafkaTransportExpression AddKafkaBroker<TOption>()
        where TOption : KafkaBrokerOption, new()
    {
        var option = GetRequiredOption<TOption>();

        return RegisterKafkaBroker(option);
    }

    /// <summary>
    /// Registers a Kafka producer for an event type from the section named after <typeparamref name="TOption"/>.
    /// </summary>
    /// <typeparam name="TOption">The configuration model type inherited from <see cref="KafkaProducerOption"/>.</typeparam>
    /// <typeparam name="TEvent">The domain event type to publish.</typeparam>
    /// <returns>The Wolverine Kafka sender configuration for additional topic tuning.</returns>
    public KafkaSubscriberConfiguration AddKafkaProducer<TOption, TEvent>()
        where TOption : KafkaProducerOption, new()
        where TEvent : DomainEvent
    {
        var option = GetRequiredOption<TOption>();

        return RegisterKafkaProducer<TEvent>(option);
    }

    /// <summary>
    /// Registers a Kafka consumer for an event type from the section named after <typeparamref name="TOption"/>.
    /// </summary>
    /// <typeparam name="TOption">The configuration model type inherited from <see cref="KafkaConsumerOption"/>.</typeparam>
    /// <typeparam name="TEvent">The domain event type consumed from the topic.</typeparam>
    /// <returns>The Wolverine Kafka listener configuration for additional topic tuning.</returns>
    public KafkaListenerConfiguration AddKafkaConsumer<TOption, TEvent>()
        where TOption : KafkaConsumerOption, new()
        where TEvent : DomainEvent
    {
        var option = GetRequiredOption<TOption>();

        return RegisterKafkaConsumer<TEvent>(option);
    }

    private KafkaTransportExpression RegisterKafkaBroker(KafkaBrokerOption option)
    {
        if (string.IsNullOrWhiteSpace(option.BootstrapServers))
        {
            throw new ArgumentException(
                "The Kafka bootstrap servers value must not be empty.",
                nameof(option));
        }

        return string.IsNullOrWhiteSpace(option.BrokerName)
            ? wolverineOptions.UseKafka(option.BootstrapServers)
            : wolverineOptions.AddNamedKafkaBroker(
                new BrokerName(option.BrokerName),
                option.BootstrapServers);
    }

    private KafkaSubscriberConfiguration RegisterKafkaProducer<TEvent>(KafkaProducerOption option)
        where TEvent : DomainEvent
    {
        ValidateTopicName(option.TopicName);

        var publisher = wolverineOptions.PublishMessage<TEvent>();
        var subscriber = string.IsNullOrWhiteSpace(option.BrokerName)
            ? publisher.ToKafkaTopic(option.TopicName)
            : publisher.ToKafkaTopicOnNamedBroker(
                new BrokerName(option.BrokerName),
                option.TopicName);

        return subscriber.UseDurableOutbox();
    }

    private KafkaListenerConfiguration RegisterKafkaConsumer<TEvent>(KafkaConsumerOption option)
        where TEvent : DomainEvent
    {
        ValidateTopicName(option.TopicName);

        var listener = string.IsNullOrWhiteSpace(option.BrokerName)
            ? wolverineOptions.ListenToKafkaTopic(option.TopicName)
            : wolverineOptions.ListenToKafkaTopicOnNamedBroker(
                new BrokerName(option.BrokerName),
                option.TopicName);

        listener.DefaultIncomingMessage<TEvent>();

        if (!string.IsNullOrWhiteSpace(option.ConsumerGroupId) ||
            option.AutoOffsetReset.HasValue)
        {
            listener.ConfigureConsumer(config =>
            {
                if (!string.IsNullOrWhiteSpace(option.ConsumerGroupId))
                {
                    config.GroupId = option.ConsumerGroupId;
                }

                if (option.AutoOffsetReset.HasValue)
                {
                    config.AutoOffsetReset = option.AutoOffsetReset.Value;
                }
            });
        }

        return listener.UseDurableInbox();
    }

    private TOption GetRequiredOption<TOption>()
        where TOption : new()
    {
        var sectionName = typeof(TOption).Name;
        var section = configuration.GetSection(sectionName);

        if (!section.Exists())
        {
            throw new InvalidOperationException(
                $"Configuration section '{sectionName}' was not found.");
        }

        var option = section.Get<TOption>();
        return option is null
            ? throw new InvalidOperationException(
                $"Configuration section '{sectionName}' could not be bound to '{typeof(TOption).FullName}'.")
            : option;
    }

    private static void ValidateTopicName(string topicName)
    {
        if (string.IsNullOrWhiteSpace(topicName))
        {
            throw new ArgumentException(
                "The Kafka topic name must not be empty.",
                nameof(topicName));
        }
    }
}
