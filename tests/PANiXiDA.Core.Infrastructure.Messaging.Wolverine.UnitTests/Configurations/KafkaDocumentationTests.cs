using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.ApiDocumentation;
using PANiXiDA.Core.ApiDocumentation.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Configurations;

using Wolverine;
using Wolverine.Configuration;
using Wolverine.Kafka;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class KafkaDocumentationTests
{
    [Theory(DisplayName = "Convention and typed Kafka registrations expose friendly operation names without changing topics or groups")]
    [InlineData(false)]
    [InlineData(true)]
    public void DescribesOperationMetadata(bool typed)
    {
        var configuration = CreateConfiguration();
        configuration["TestConsumerOption:TopicName"] = "typed-events";
        configuration["TestConsumerOption:ConsumerGroupId"] = "typed-group";
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, configuration);
        kafka.AddKafkaBroker();
        kafka.AddKafkaBroker<TestBrokerOption>();
        kafka.WithDocumentation("module", "1.0", module =>
        {
            var producer = new ApiOperationMetadata("Publish event", "Publishes the event.");
            var consumer = new ApiOperationMetadata("Consume event", "Consumes the event.");
            if (typed)
            {
                module.AddKafkaProducer<TestProducerOption, TestDomainEvent>(producer);
                ((IDelayedEndpointConfiguration)module.AddKafkaConsumer<TestConsumerOption, TestDomainEvent>(consumer)).Apply();
            }
            else
            {
                module.AddProducer<TestDomainEvent>(producer);
                ((IDelayedEndpointConfiguration)module.AddConsumer<TestDomainEvent>(consumer)).Apply();
            }
        });
        options.Services.AddApiDocumentationSchemas(KafkaJsonContext.Default);
        using var provider = options.Services.BuildServiceProvider();
        var source = provider.GetServices<IApiDocumentSource>().OfType<IAsyncApiDocumentSource>().Single();

        var document = source.Generate("module-kafka-v1", TestContext.Current.CancellationToken);

        var send = document["operations"]!.AsObject().Single(operation => operation.Value!["action"]!.GetValue<string>() == "send");
        var receive = document["operations"]!.AsObject().Single(operation => operation.Value!["action"]!.GetValue<string>() == "receive");
        send.Key.ShouldContain(typeof(TestDomainEvent).FullName!);
        send.Value!["title"]!.GetValue<string>().ShouldBe("Publish event");
        send.Value["summary"]!.GetValue<string>().ShouldBe("Publishes the event.");
        receive.Value!["title"]!.GetValue<string>().ShouldBe("Consume event");
        receive.Value["summary"]!.GetValue<string>().ShouldBe("Consumes the event.");
        receive.Value["bindings"]!["kafka"]!["groupId"]!["enum"]![0]!.GetValue<string>().ShouldBe(typed ? "typed-group" : "matchmaking");
        document["channels"]!.AsObject().Select(channel => channel.Value!["address"]!.GetValue<string>()).ShouldContain(typed ? "typed-events" : "events");
        Should.Throw<ArgumentNullException>(() => kafka.AddProducer<TestDomainEvent>((ApiOperationMetadata)null!));
        Should.Throw<ArgumentNullException>(() => kafka.AddConsumer<TestDomainEvent>((ApiOperationMetadata)null!));
        Should.Throw<ArgumentNullException>(() => kafka.AddKafkaProducer<TestProducerOption, TestDomainEvent>(null!));
        Should.Throw<ArgumentNullException>(() => kafka.AddKafkaConsumer<TestConsumerOption, TestDomainEvent>(null!));
    }

    [Fact(DisplayName = "Kafka documentation captures actual typed topology and isolates modules versions and named brokers")]
    public void CapturesTopology()
    {
        var configuration = CreateConfiguration();
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, configuration);
        kafka.AddKafkaBroker();
        kafka.AddKafkaBroker<TestBrokerOption>();
        kafka.WithDocumentation("matchmaking", "1.0", module =>
        {
            module.AddProducer<TestDomainEvent>();
            ((IDelayedEndpointConfiguration)module.AddConsumer<TestDomainEvent>()).Apply();
            module.AddKafkaProducer<TestProducerOption, TestDomainEvent>();
        }, "Matchmaking").ShouldBeSameAs(kafka);
        kafka.WithDocumentation("matchmaking", "2.0", module => module.AddProducer<TestDomainEvent>(), deprecated: true);
        kafka.WithDocumentation("battle", "1.0", module => module.AddProducer<TestDomainEvent>());
        options.Services.AddApiDocumentationSchemas(KafkaJsonContext.Default);
        using var provider = options.Services.BuildServiceProvider();
        var source = provider.GetServices<IApiDocumentSource>().OfType<IAsyncApiDocumentSource>().Single();
        source.GetDocuments().Select(document => document.Name).Order().ShouldBe(["battle-kafka-v1", "matchmaking-kafka-v1", "matchmaking-kafka-v2"]);
        var document = source.Generate("matchmaking-kafka-v1", TestContext.Current.CancellationToken);
        document["channels"]!.AsObject().Count.ShouldBe(2);
        document["operations"]!.AsObject().Count.ShouldBe(3);
        document["components"]!["messages"]!.AsObject().Count.ShouldBe(1);
        document["servers"]!["default"]!["host"]!.GetValue<string>().ShouldBe("primary:9092");
        var receive = document["operations"]!.AsObject().Single(operation => operation.Value!["action"]!.GetValue<string>() == "receive").Value!;
        receive["title"]!.GetValue<string>().ShouldBe("Consume TestDomainEvent");
        receive["bindings"]!["kafka"]!["groupId"]!["enum"]![0]!.GetValue<string>().ShouldBe("matchmaking");
        source.Generate("matchmaking-kafka-v2", TestContext.Current.CancellationToken)["info"]!["x-deprecated"]!.GetValue<bool>().ShouldBeTrue();
        document["components"]!["messages"]!.AsObject().Single().Value!["payload"]!["schema"]!["properties"]!["eventId"].ShouldNotBeNull();
        Should.Throw<ArgumentException>(() => source.Generate("missing"));
        Should.Throw<ArgumentException>(() => source.Generate(""));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Should.Throw<OperationCanceledException>(() => source.Generate("battle-kafka-v1", cancelled.Token));
    }

    [Fact(DisplayName = "Kafka documentation leaves existing ungrouped registrations unchanged and validates metadata")]
    public void PreservesExistingRegistrations()
    {
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, CreateConfiguration());
        kafka.AddKafkaBroker();
        kafka.AddProducer<TestDomainEvent>();
        _ = new WolverineKafkaConfiguration(options, CreateConfiguration());
        using var provider = options.Services.BuildServiceProvider();
        provider.GetServices<IApiDocumentSource>().Count().ShouldBe(1);
        provider.GetRequiredService<ApiDocumentCatalog>().GetDocuments().ShouldBeEmpty();
        Should.Throw<ArgumentNullException>(() => kafka.WithDocumentation("module", "1.0", null!));
        Should.Throw<ArgumentException>(() => kafka.WithDocumentation("", "1.0", _ => { }));
        Should.Throw<ArgumentException>(() => kafka.WithDocumentation("module", "", _ => { }));
        Should.Throw<ArgumentException>(() => kafka.WithDocumentation("module", "1.0", _ => { }, ""));
    }

    [Theory(DisplayName = "Kafka documentation reports missing brokers or generated schema metadata")]
    [InlineData(false)]
    [InlineData(true)]
    public void ReportsIncompleteRegistration(bool registerBroker)
    {
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, CreateConfiguration());
        if (registerBroker)
        {
            kafka.AddKafkaBroker();
        }

        kafka.WithDocumentation("module", "1.0", module => module.AddProducer<TestDomainEvent>());
        using var provider = options.Services.BuildServiceProvider();
        var source = provider.GetServices<IApiDocumentSource>().OfType<IAsyncApiDocumentSource>().Single();
        var error = Should.Throw<InvalidOperationException>(() => source.Generate("module-kafka-v1"));
        error.Message.ShouldContain(registerBroker ? "Source-generated JSON metadata" : "broker");
    }

    [Fact(DisplayName = "Kafka documentation rejects conflicting metadata for the same module version")]
    public void RejectsConflicts()
    {
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, CreateConfiguration());
        kafka.WithDocumentation("module", "1.0", module => module.AddProducer<TestDomainEvent>(), "First");
        kafka.WithDocumentation("module", "1.0", module => module.AddProducer<TestDomainEvent>(), "Second");
        using var provider = options.Services.BuildServiceProvider();
        Should.Throw<InvalidOperationException>(() => provider.GetRequiredService<ApiDocumentCatalog>().GetDocuments());
    }

    [Theory(DisplayName = "Kafka documentation reads consumer groups after listener customization")]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData("configured", true)]
    public void ReadsFinalConsumerGroup(string? configuredGroup, bool customize)
    {
        var configuration = CreateConfiguration();
        configuration["Messaging:Consumers:TestDomainEvent:ConsumerGroupId"] = configuredGroup;
        var options = new WolverineOptions();
        var kafka = new WolverineKafkaConfiguration(options, configuration);
        kafka.AddKafkaBroker();
        KafkaListenerConfiguration? listener = null;
        kafka.WithDocumentation("module", "1.0", module =>
        {
            listener = module.AddConsumer<TestDomainEvent>();
            if (customize)
            {
                listener.ConfigureConsumer(consumer => consumer.GroupId = "override");
            }
        });
        ((IDelayedEndpointConfiguration)listener!).Apply();
        options.Services.AddApiDocumentationSchemas(KafkaJsonContext.Default);
        using var provider = options.Services.BuildServiceProvider();
        var source = provider.GetServices<IApiDocumentSource>().OfType<IAsyncApiDocumentSource>().Single();
        var operation = source.Generate("module-kafka-v1", TestContext.Current.CancellationToken)["operations"]!.AsObject().Single().Value!;
        if (customize)
        {
            operation["bindings"]!["kafka"]!["groupId"]!["enum"]![0]!.GetValue<string>().ShouldBe("override");
        }
        else
        {
            operation["bindings"].ShouldBeNull();
        }
    }

    private static IConfiguration CreateConfiguration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Messaging:Kafka:BootstrapServers"] = "primary:9092,replica:9092",
        ["Messaging:Producers:TestDomainEvent:TopicName"] = "events",
        ["Messaging:Consumers:TestDomainEvent:TopicName"] = "events",
        ["Messaging:Consumers:TestDomainEvent:ConsumerGroupId"] = "matchmaking",
        ["TestBrokerOption:BrokerName"] = "external",
        ["TestBrokerOption:BootstrapServers"] = "external:9092",
        ["TestProducerOption:BrokerName"] = "external",
        ["TestProducerOption:TopicName"] = "events"
    }).Build();
}

[JsonSerializable(typeof(TestDomainEvent))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class KafkaJsonContext : JsonSerializerContext;
