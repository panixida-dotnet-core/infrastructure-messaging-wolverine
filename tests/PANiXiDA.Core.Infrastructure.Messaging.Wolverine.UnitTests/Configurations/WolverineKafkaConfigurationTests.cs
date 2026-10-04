using Confluent.Kafka;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Configurations;

using Wolverine;

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
