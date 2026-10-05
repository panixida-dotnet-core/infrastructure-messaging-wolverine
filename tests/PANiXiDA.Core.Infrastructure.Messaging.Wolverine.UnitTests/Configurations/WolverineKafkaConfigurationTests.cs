using Confluent.Kafka;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Configurations;

using Wolverine;
using Wolverine.Configuration;
using Wolverine.Kafka;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class WolverineKafkaConfigurationTests
{
    [Fact(DisplayName = "Kafka convention methods read the standard messaging sections")]
    public void ConventionMethodsShouldReadStandardSections()
    {
        var configuration = CreateConventionConfiguration("Messaging");
        var kafka = new WolverineKafkaConfiguration(new WolverineOptions(), configuration);

        var broker = kafka.AddKafkaBroker();
        var producer = kafka.AddProducer<TestDomainEvent>();
        var consumer = kafka.AddConsumer<TestDomainEvent>();

        broker.ShouldNotBeNull();
        producer.ShouldNotBeNull();
        consumer.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Kafka convention methods allow custom configuration sections and named brokers")]
    public void ConventionMethodsShouldReadCustomSections()
    {
        var configuration = CreateConventionConfiguration("External");
        configuration["External:Kafka:BrokerName"] = "external";
        configuration["External:Producers:TestDomainEvent:BrokerName"] = "external";
        configuration["External:Consumers:TestDomainEvent:BrokerName"] = "external";
        var kafka = new WolverineKafkaConfiguration(new WolverineOptions(), configuration);

        var broker = kafka.AddKafkaBroker("External:Kafka");
        var producer = kafka.AddProducer<TestDomainEvent>("External:Producers");
        var consumer = kafka.AddConsumer<TestDomainEvent>("External:Consumers");

        broker.ShouldNotBeNull();
        producer.ShouldNotBeNull();
        consumer.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Kafka convention methods report missing sections without falling back to typed options")]
    public void ConventionMethodsShouldRejectMissingSections()
    {
        var kafka = new WolverineKafkaConfiguration(new WolverineOptions(), new ConfigurationManager());

        var brokerError = Should.Throw<InvalidOperationException>(() => kafka.AddKafkaBroker());
        var producerError = Should.Throw<InvalidOperationException>(() => kafka.AddProducer<TestDomainEvent>());
        var consumerError = Should.Throw<InvalidOperationException>(() => kafka.AddConsumer<TestDomainEvent>());

        brokerError.Message.ShouldBe("Configuration section 'Messaging:Kafka' was not found.");
        producerError.Message.ShouldBe("Configuration section 'Messaging:Producers:TestDomainEvent' was not found.");
        consumerError.Message.ShouldBe("Configuration section 'Messaging:Consumers:TestDomainEvent' was not found.");
    }

    private static ConfigurationManager CreateConventionConfiguration(string rootSection)
    {
        return CreateConfiguration(
            ($"{rootSection}:Kafka:BootstrapServers", "localhost:9092"),
            ($"{rootSection}:Producers:TestDomainEvent:TopicName", "produced-events"),
            ($"{rootSection}:Consumers:TestDomainEvent:TopicName", "consumed-events"),
            ($"{rootSection}:Consumers:TestDomainEvent:ConsumerGroupId", "test-group"),
            ($"{rootSection}:Consumers:TestDomainEvent:AutoOffsetReset", "Earliest"));
    }

    [Fact(DisplayName = "AddKafkaBroker registers default broker from option section")]
    public void AddKafkaBrokerShouldRegisterDefaultBrokerFromOptionSection()
    {
        var configuration = CreateConfiguration(
            ("TestBrokerOption:BootstrapServers", "localhost:9092"));
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, configuration);

        void act()
        {
            kafka.AddKafkaBroker<TestBrokerOption>();
        }

        Should.NotThrow(act);
    }

    [Fact(DisplayName = "AddKafkaBroker registers named broker from option section")]
    public void AddKafkaBrokerShouldRegisterNamedBrokerFromOptionSection()
    {
        var configuration = CreateConfiguration(
            ("TestBrokerOption:BrokerName", "external"),
            ("TestBrokerOption:BootstrapServers", "localhost:9092"));
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, configuration);

        void act()
        {
            kafka.AddKafkaBroker<TestBrokerOption>();
        }

        Should.NotThrow(act);
    }

    [Fact(DisplayName = "AddKafkaBroker rejects missing option section")]
    public void AddKafkaBrokerShouldRejectMissingOptionSection()
    {
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, new ConfigurationManager());

        void act()
        {
            kafka.AddKafkaBroker<TestBrokerOption>();
        }

        var exception = Should.Throw<InvalidOperationException>(act);

        exception.Message.ShouldBe("Configuration section 'TestBrokerOption' was not found.");
    }

    [Fact(DisplayName = "AddKafkaBroker rejects an option section that cannot be bound")]
    public void AddKafkaBrokerShouldRejectOptionSectionThatCannotBeBound()
    {
        var configuration = CreateConfiguration(
            ("TestBrokerOption", string.Empty));
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, configuration);

        void act()
        {
            kafka.AddKafkaBroker<TestBrokerOption>();
        }

        var exception = Should.Throw<InvalidOperationException>(act);

        exception.Message.ShouldBe(
            "Configuration section 'TestBrokerOption' could not be bound to 'PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Configurations.TestBrokerOption'.");
    }

    [Fact(DisplayName = "AddKafkaBroker rejects blank bootstrap servers")]
    public void AddKafkaBrokerShouldRejectBlankBootstrapServers()
    {
        var configuration = CreateConfiguration(
            ("TestBrokerOption:BootstrapServers", " "));
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, configuration);

        void act()
        {
            kafka.AddKafkaBroker<TestBrokerOption>();
        }

        var exception = Should.Throw<ArgumentException>(act);

        exception.Message.ShouldBe("The Kafka bootstrap servers value must not be empty. (Parameter 'option')");
    }

    [Fact(DisplayName = "AddKafkaProducer registers durable producer for default broker")]
    public void AddKafkaProducerShouldRegisterDurableProducerForDefaultBroker()
    {
        var configuration = CreateConfiguration(
            ("TestProducerOption:TopicName", "test-events"));
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, configuration);

        void act()
        {
            kafka.AddKafkaProducer<TestProducerOption, TestDomainEvent>();
        }

        Should.NotThrow(act);
    }

    [Fact(DisplayName = "AddKafkaProducer registers durable producer for named broker")]
    public void AddKafkaProducerShouldRegisterDurableProducerForNamedBroker()
    {
        var configuration = CreateConfiguration(
            ("TestProducerOption:BrokerName", "external"),
            ("TestProducerOption:TopicName", "test-events"));
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, configuration);

        void act()
        {
            kafka.AddKafkaProducer<TestProducerOption, TestDomainEvent>();
        }

        Should.NotThrow(act);
    }

    [Fact(DisplayName = "AddKafkaProducer rejects blank topic name")]
    public void AddKafkaProducerShouldRejectBlankTopicName()
    {
        var configuration = CreateConfiguration(
            ("TestProducerOption:TopicName", " "));
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, configuration);

        void act()
        {
            kafka.AddKafkaProducer<TestProducerOption, TestDomainEvent>();
        }

        var exception = Should.Throw<ArgumentException>(act);

        exception.Message.ShouldBe("The Kafka topic name must not be empty. (Parameter 'topicName')");
    }

    [Fact(DisplayName = "AddKafkaConsumer registers durable consumer with optional consumer settings")]
    public void AddKafkaConsumerShouldRegisterDurableConsumerWithOptionalConsumerSettings()
    {
        var configuration = CreateConfiguration(
            ("TestConsumerOption:TopicName", "test-events"),
            ("TestConsumerOption:ConsumerGroupId", "test-group"),
            ("TestConsumerOption:AutoOffsetReset", AutoOffsetReset.Earliest.ToString()));
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, configuration);

        void act()
        {
            kafka.AddKafkaConsumer<TestConsumerOption, TestDomainEvent>();
        }

        Should.NotThrow(act);
    }

    [Fact(DisplayName = "AddKafkaConsumer registers durable consumer for named broker")]
    public void AddKafkaConsumerShouldRegisterDurableConsumerForNamedBroker()
    {
        var configuration = CreateConfiguration(
            ("TestConsumerOption:BrokerName", "external"),
            ("TestConsumerOption:TopicName", "test-events"));
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, configuration);

        void act()
        {
            kafka.AddKafkaConsumer<TestConsumerOption, TestDomainEvent>();
        }

        Should.NotThrow(act);
    }

    [Theory(DisplayName = "Kafka consumer registration maps the configured group protocol and preserves consumer settings")]
    [InlineData(false, null, null)]
    [InlineData(true, null, null)]
    [InlineData(false, "Classic", null)]
    [InlineData(true, "Classic", null)]
    [InlineData(false, "Consumer", null)]
    [InlineData(true, "Consumer", null)]
    [InlineData(false, "Consumer", "external")]
    [InlineData(true, "Consumer", "external")]
    public void ConsumerRegistrationShouldMapGroupProtocol(bool useConventions, string? groupProtocol, string? brokerName)
    {
        var section = useConventions ? "Messaging:Consumers:TestDomainEvent" : "TestConsumerOption";
        var configuration = CreateConfiguration(
            ($"{section}:TopicName", "test-events"),
            ($"{section}:BrokerName", brokerName),
            ($"{section}:ConsumerGroupId", "test-group"),
            ($"{section}:AutoOffsetReset", "Earliest"),
            ($"{section}:GroupProtocol", groupProtocol));
        var kafka = new WolverineKafkaConfiguration(new WolverineOptions(), configuration);

        var listener = useConventions
            ? kafka.AddConsumer<TestDomainEvent>()
            : kafka.AddKafkaConsumer<TestConsumerOption, TestDomainEvent>();
        ((IDelayedEndpointConfiguration)listener).Apply();

        var topic = listener.Endpoint.ShouldBeOfType<KafkaTopic>();
        topic.Mode.ShouldBe(EndpointMode.Durable);
        var consumerConfig = topic.ConsumerConfig.ShouldNotBeNull();
        consumerConfig.GroupId.ShouldBe("test-group");
        consumerConfig.AutoOffsetReset.ShouldBe(AutoOffsetReset.Earliest);
        consumerConfig.GroupProtocol.ShouldBe(groupProtocol is null ? null : Enum.Parse<GroupProtocol>(groupProtocol));
    }

    [Theory(DisplayName = "Kafka consumers apply the group protocol without other optional consumer settings")]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsumerRegistrationShouldApplyGroupProtocolWithoutOtherSettings(bool useConventions)
    {
        var section = useConventions ? "Messaging:Consumers:TestDomainEvent" : "TestConsumerOption";
        var configuration = CreateConfiguration(
            ($"{section}:TopicName", "test-events"),
            ($"{section}:GroupProtocol", "Consumer"));
        var kafka = new WolverineKafkaConfiguration(new WolverineOptions(), configuration);

        var listener = useConventions
            ? kafka.AddConsumer<TestDomainEvent>()
            : kafka.AddKafkaConsumer<TestConsumerOption, TestDomainEvent>();
        ((IDelayedEndpointConfiguration)listener).Apply();

        var topic = listener.Endpoint.ShouldBeOfType<KafkaTopic>();
        topic.ConsumerConfig.ShouldNotBeNull().GroupProtocol.ShouldBe(GroupProtocol.Consumer);
    }

    [Theory(DisplayName = "Kafka consumers reject an invalid group protocol during configuration binding")]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsumerRegistrationShouldRejectInvalidGroupProtocol(bool useConventions)
    {
        var section = useConventions ? "Messaging:Consumers:TestDomainEvent" : "TestConsumerOption";
        var configuration = CreateConfiguration(
            ($"{section}:TopicName", "test-events"),
            ($"{section}:GroupProtocol", "Invalid"));
        var kafka = new WolverineKafkaConfiguration(new WolverineOptions(), configuration);

        void act()
        {
            if (useConventions)
            {
                kafka.AddConsumer<TestDomainEvent>();
            }
            else
            {
                kafka.AddKafkaConsumer<TestConsumerOption, TestDomainEvent>();
            }
        }

        var error = Should.Throw<InvalidOperationException>(act);
        error.Message.ShouldContain($"{section}:GroupProtocol");
    }

    private static ConfigurationManager CreateConfiguration(params (string Key, string? Value)[] values)
    {
        var configuration = new ConfigurationManager();

        for (var i = 0; i < values.Length; i++)
        {
            configuration[values[i].Key] = values[i].Value;
        }

        return configuration;
    }
}
