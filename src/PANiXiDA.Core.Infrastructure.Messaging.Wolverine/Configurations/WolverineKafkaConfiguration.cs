using PANiXiDA.Core.ApiDocumentation.AsyncApi;
using PANiXiDA.Core.ApiDocumentation;
using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Documentation;

using Wolverine;
using Wolverine.Kafka;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;

/// <summary>
/// Builds Wolverine Kafka broker, producer, and consumer topology from configuration sections.
/// </summary>
public sealed class WolverineKafkaConfiguration
{
    private readonly WolverineOptions wolverineOptions;
    private readonly IConfiguration configuration;
    private readonly KafkaDocumentationRegistry documentation;
    private readonly ApiDocumentDescriptor? document;

    internal WolverineKafkaConfiguration(
        WolverineOptions wolverineOptions,
        IConfiguration configuration)
    {
        this.wolverineOptions = wolverineOptions;
        this.configuration = configuration;
        documentation = KafkaDocumentationRegistry.Register(wolverineOptions.Services);
    }

    private WolverineKafkaConfiguration(WolverineKafkaConfiguration parent, ApiDocumentDescriptor document)
    {
        wolverineOptions = parent.wolverineOptions;
        configuration = parent.configuration;
        documentation = parent.documentation;
        this.document = document;
    }

    /// <summary>Groups typed Kafka registrations into a versioned module document.</summary>
    /// <param name="module">The stable module name used in document URLs.</param>
    /// <param name="version">The message contract version. This does not change topic names or routing.</param>
    /// <param name="configure">The existing producer and consumer registrations for this module.</param>
    /// <param name="title">An optional display title.</param>
    /// <param name="deprecated">Whether the contract version is deprecated.</param>
    /// <returns>The original Kafka configuration.</returns>
    public WolverineKafkaConfiguration WithDocumentation(string module, string version,
        Action<WolverineKafkaConfiguration> configure, string? title = null, bool deprecated = false)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (title is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(title);
        }
        var descriptor = new ApiDocumentDescriptor(ApiDocumentNames.Create(module, "kafka", version),
            $"{title ?? module} Kafka v{version}", module, version, ApiDocumentFormat.AsyncApi, deprecated: deprecated);
        configure(new WolverineKafkaConfiguration(this, descriptor));
        return this;
    }

    /// <summary>
    /// Registers a durable Kafka producer from the child section named after the event type.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type to publish.</typeparam>
    /// <param name="parentSectionName">The parent configuration section. Defaults to Messaging:Producers.</param>
    /// <returns>The Wolverine Kafka sender configuration for additional topic tuning.</returns>
    public KafkaSubscriberConfiguration AddProducer<TEvent>(string parentSectionName = "Messaging:Producers")
        where TEvent : DomainEvent
    {
        var option = GetRequiredOption<KafkaProducerOption>($"{parentSectionName}:{typeof(TEvent).Name}");
        return RegisterKafkaProducer<TEvent>(option);
    }

    /// <summary>Registers a Kafka producer with an explicit operation display name and summary.</summary>
    /// <typeparam name="TEvent">The published domain event.</typeparam>
    /// <param name="operation">The documentation metadata; transport routing is unchanged.</param>
    /// <param name="parentSectionName">The parent producer configuration section.</param>
    /// <returns>The Wolverine sender configuration.</returns>
    public KafkaSubscriberConfiguration AddProducer<TEvent>(ApiOperationMetadata operation,
        string parentSectionName = "Messaging:Producers") where TEvent : DomainEvent
    {
        ArgumentNullException.ThrowIfNull(operation);
        return RegisterKafkaProducer<TEvent>(GetRequiredOption<KafkaProducerOption>($"{parentSectionName}:{typeof(TEvent).Name}"), operation);
    }

    /// <summary>
    /// Registers a durable Kafka consumer from the child section named after the event type.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type consumed from the topic.</typeparam>
    /// <param name="parentSectionName">The parent configuration section. Defaults to Messaging:Consumers.</param>
    /// <returns>The Wolverine Kafka listener configuration for additional topic tuning.</returns>
    public KafkaListenerConfiguration AddConsumer<TEvent>(string parentSectionName = "Messaging:Consumers")
        where TEvent : DomainEvent
    {
        var option = GetRequiredOption<KafkaConsumerOption>($"{parentSectionName}:{typeof(TEvent).Name}");
        return RegisterKafkaConsumer<TEvent>(option);
    }

    /// <summary>Registers a Kafka consumer with an explicit operation display name and summary.</summary>
    /// <typeparam name="TEvent">The consumed domain event.</typeparam>
    /// <param name="operation">The documentation metadata; transport routing is unchanged.</param>
    /// <param name="parentSectionName">The parent consumer configuration section.</param>
    /// <returns>The Wolverine listener configuration.</returns>
    public KafkaListenerConfiguration AddConsumer<TEvent>(ApiOperationMetadata operation,
        string parentSectionName = "Messaging:Consumers") where TEvent : DomainEvent
    {
        ArgumentNullException.ThrowIfNull(operation);
        return RegisterKafkaConsumer<TEvent>(GetRequiredOption<KafkaConsumerOption>($"{parentSectionName}:{typeof(TEvent).Name}"), operation);
    }

    /// <summary>
    /// Registers a Kafka broker from the specified configuration section.
    /// </summary>
    /// <param name="sectionName">The broker configuration section. Defaults to Messaging:Kafka.</param>
    /// <returns>The Wolverine Kafka transport expression for additional configuration.</returns>
    public KafkaTransportExpression AddKafkaBroker(string sectionName = "Messaging:Kafka")
    {
        return RegisterKafkaBroker(GetRequiredOption<KafkaBrokerOption>(sectionName));
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

    /// <summary>Registers a typed-options Kafka producer with explicit operation documentation.</summary>
    /// <typeparam name="TOption">The producer configuration model.</typeparam>
    /// <typeparam name="TEvent">The published domain event.</typeparam>
    /// <param name="operation">The operation display name and optional summary.</param>
    /// <returns>The Wolverine sender configuration.</returns>
    public KafkaSubscriberConfiguration AddKafkaProducer<TOption, TEvent>(ApiOperationMetadata operation)
        where TOption : KafkaProducerOption, new()
        where TEvent : DomainEvent
    {
        ArgumentNullException.ThrowIfNull(operation);
        return RegisterKafkaProducer<TEvent>(GetRequiredOption<TOption>(), operation);
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

    /// <summary>Registers a typed-options Kafka consumer with explicit operation documentation.</summary>
    /// <typeparam name="TOption">The consumer configuration model.</typeparam>
    /// <typeparam name="TEvent">The consumed domain event.</typeparam>
    /// <param name="operation">The operation display name and optional summary.</param>
    /// <returns>The Wolverine listener configuration.</returns>
    public KafkaListenerConfiguration AddKafkaConsumer<TOption, TEvent>(ApiOperationMetadata operation)
        where TOption : KafkaConsumerOption, new()
        where TEvent : DomainEvent
    {
        ArgumentNullException.ThrowIfNull(operation);
        return RegisterKafkaConsumer<TEvent>(GetRequiredOption<TOption>(), operation);
    }

    private KafkaTransportExpression RegisterKafkaBroker(KafkaBrokerOption option)
    {
        if (string.IsNullOrWhiteSpace(option.BootstrapServers))
        {
            throw new ArgumentException(
                "The Kafka bootstrap servers value must not be empty.",
                nameof(option));
        }

        documentation.Brokers[BrokerNameForDocumentation(option.BrokerName)] = option.BootstrapServers;
        return string.IsNullOrWhiteSpace(option.BrokerName)
            ? wolverineOptions.UseKafka(option.BootstrapServers)
            : wolverineOptions.AddNamedKafkaBroker(
                new BrokerName(option.BrokerName),
                option.BootstrapServers);
    }

    private KafkaSubscriberConfiguration RegisterKafkaProducer<TEvent>(KafkaProducerOption option, ApiOperationMetadata? metadata = null)
        where TEvent : DomainEvent
    {
        ValidateTopicName(option.TopicName);

        var publisher = wolverineOptions.PublishMessage<TEvent>();
        var subscriber = string.IsNullOrWhiteSpace(option.BrokerName)
            ? publisher.ToKafkaTopic(option.TopicName)
            : publisher.ToKafkaTopicOnNamedBroker(
                new BrokerName(option.BrokerName),
                option.TopicName);

        var result = subscriber.UseDurableOutbox();
        RecordOperation<TEvent>(option.TopicName, option.BrokerName, AsyncApiAction.Send, metadata: metadata);
        return result;
    }

    private KafkaListenerConfiguration RegisterKafkaConsumer<TEvent>(KafkaConsumerOption option, ApiOperationMetadata? metadata = null)
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
            option.AutoOffsetReset.HasValue ||
            option.GroupProtocol.HasValue)
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

                if (option.GroupProtocol.HasValue)
                {
                    config.GroupProtocol = option.GroupProtocol.Value;
                }
            });
        }

        var result = listener.UseDurableInbox();
        RecordOperation<TEvent>(option.TopicName, option.BrokerName, AsyncApiAction.Receive, () => ((KafkaTopic)listener.Endpoint).ConsumerConfig?.GroupId, metadata);
        return result;
    }

    private void RecordOperation<TEvent>(string topic, string? broker, AsyncApiAction action, Func<string?>? group = null,
        ApiOperationMetadata? metadata = null)
    {
        if (document is not null)
        {
            documentation.Operations.Add(new KafkaOperationDescription((ApiDocumentDescriptor)document, typeof(TEvent), topic,
                BrokerNameForDocumentation(broker), action, group, metadata));
        }
    }

    private static string BrokerNameForDocumentation(string? broker) => string.IsNullOrWhiteSpace(broker) ? "default" : "named:" + broker;

    private TOption GetRequiredOption<TOption>()
        where TOption : new()
    {
        return GetRequiredOption<TOption>(typeof(TOption).Name);
    }

    private TOption GetRequiredOption<TOption>(string sectionName)
        where TOption : new()
    {
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
