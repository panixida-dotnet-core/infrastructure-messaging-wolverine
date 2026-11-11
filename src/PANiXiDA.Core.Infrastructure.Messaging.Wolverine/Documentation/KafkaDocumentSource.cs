using PANiXiDA.Core.ApiDocumentation.DependencyInjection;
using PANiXiDA.Core.ApiDocumentation.AsyncApi;
using PANiXiDA.Core.ApiDocumentation;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Documentation;

internal sealed class KafkaDocumentationRegistry
{
    internal Dictionary<string, string> Brokers { get; } = new(StringComparer.Ordinal);

    internal List<KafkaOperationDescription> Operations { get; } = [];

    internal static KafkaDocumentationRegistry Register(IServiceCollection services)
    {
        var existing = services.LastOrDefault(service => service.ServiceType == typeof(KafkaDocumentationRegistry));
        if (existing?.ImplementationInstance is KafkaDocumentationRegistry registered)
        {
            return registered;
        }

        var registry = new KafkaDocumentationRegistry();
        services.AddSingleton(registry);
        services.AddApiDocumentSource<KafkaDocumentSource>();
        return registry;
    }
}

internal sealed record KafkaOperationDescription(ApiDocumentDescriptor Document, Type EventType,
    string Topic, string Broker, AsyncApiAction Action, Func<string?>? ConsumerGroup, ApiOperationMetadata? Metadata = null);

internal sealed class KafkaDocumentSource(KafkaDocumentationRegistry registry, ApiSchemaRegistry schemas) : IAsyncApiDocumentSource
{
    public IReadOnlyCollection<ApiDocumentDescriptor> GetDocuments()
    {
        return [.. registry.Operations.Select(operation => operation.Document).GroupBy(document => document.Name)
            .Select(group =>
            {
                var first = group.First();
                if (group.Any(document => document.Module != first.Module || document.Version != first.Version ||
                    document.Title != first.Title || document.Deprecated != first.Deprecated))
                {
                    throw new InvalidOperationException($"Kafka document '{first.Name}' has conflicting module metadata.");
                }

                return first;
            })];
    }

    public JsonObject Generate(string documentName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);
        cancellationToken.ThrowIfCancellationRequested();
        var document = GetDocuments().SingleOrDefault(document => document.Name == documentName)
            ?? throw new ArgumentException($"Kafka document '{documentName}' was not found.", nameof(documentName));
        var registered = registry.Operations.Where(operation => operation.Document.Name == documentName).ToArray();
        var servers = registered.Select(operation => operation.Broker).Distinct(StringComparer.Ordinal).Select(broker =>
        {
            if (!registry.Brokers.TryGetValue(broker, out var bootstrapServers))
            {
                throw new InvalidOperationException($"Kafka broker '{broker}' was not registered through AddKafkaBroker.");
            }

            return new AsyncApiServer(broker, bootstrapServers.Split(',')[0].Trim(), "kafka");
        }).ToArray();
        var operations = registered.Select(operation =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = operation.Broker + "." + operation.Topic + "." + operation.Action + "." + operation.EventType.FullName;
            var consumerGroup = operation.ConsumerGroup?.Invoke();
            JsonObject? bindings = null;
            if (operation.Action == AsyncApiAction.Receive && !string.IsNullOrWhiteSpace(consumerGroup))
            {
                bindings = new JsonObject
                {
                    ["kafka"] = new JsonObject
                    {
                        ["groupId"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(consumerGroup) },
                        ["bindingVersion"] = "0.5.0"
                    }
                };
            }

            return new AsyncApiOperation(id, operation.Topic, operation.Action,
                new AsyncApiMessage(operation.EventType.FullName!, schemas.GetSchema(operation.EventType)),
                "kafka", operation.Metadata?.Summary)
            {
                Name = operation.Metadata?.Name ?? (operation.Action == AsyncApiAction.Send ? "Publish " : "Consume ") + operation.EventType.Name,
                Bindings = bindings,
                ServerName = operation.Broker
            };
        });
        return AsyncApiDocumentBuilder.Build(document, operations, servers);
    }
}
