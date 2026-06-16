using Confluent.Kafka;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Configurations;

using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class WolverineKafkaConfigurationTests
{
    [Fact(DisplayName = "AddKafkaBroker registers default broker from option section")]
    public void AddKafkaBrokerShouldRegisterDefaultBrokerFromOptionSection()
    {
        var configuration = CreateConfiguration(
            ("TestBrokerOption:BootstrapServers", "localhost:9092"));
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, configuration);

        var act = () => kafka.AddKafkaBroker<TestBrokerOption>();

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

        var act = () => kafka.AddKafkaBroker<TestBrokerOption>();

        Should.NotThrow(act);
    }

    [Fact(DisplayName = "AddKafkaBroker rejects missing option section")]
    public void AddKafkaBrokerShouldRejectMissingOptionSection()
    {
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, new ConfigurationManager());

        var act = () => kafka.AddKafkaBroker<TestBrokerOption>();

        var exception = Should.Throw<InvalidOperationException>(act);

        exception.Message.ShouldBe("Configuration section 'TestBrokerOption' was not found.");
    }

    [Fact(DisplayName = "AddKafkaBroker rejects blank bootstrap servers")]
    public void AddKafkaBrokerShouldRejectBlankBootstrapServers()
    {
        var configuration = CreateConfiguration(
            ("TestBrokerOption:BootstrapServers", " "));
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, configuration);

        var act = () => kafka.AddKafkaBroker<TestBrokerOption>();

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

        var act = () => kafka.AddKafkaProducer<TestProducerOption, TestDomainEvent>();

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

        var act = () => kafka.AddKafkaProducer<TestProducerOption, TestDomainEvent>();

        Should.NotThrow(act);
    }

    [Fact(DisplayName = "AddKafkaProducer rejects blank topic name")]
    public void AddKafkaProducerShouldRejectBlankTopicName()
    {
        var configuration = CreateConfiguration(
            ("TestProducerOption:TopicName", " "));
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, configuration);

        var act = () => kafka.AddKafkaProducer<TestProducerOption, TestDomainEvent>();

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

        var act = () => kafka.AddKafkaConsumer<TestConsumerOption, TestDomainEvent>();

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

        var act = () => kafka.AddKafkaConsumer<TestConsumerOption, TestDomainEvent>();

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
