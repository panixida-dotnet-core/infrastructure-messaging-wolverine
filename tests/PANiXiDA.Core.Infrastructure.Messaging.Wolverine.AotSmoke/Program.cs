using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.ApiDocumentation;
using PANiXiDA.Core.ApiDocumentation.AsyncApi;
using PANiXiDA.Core.ApiDocumentation.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.AotSmoke;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Documentation;

var services = new ServiceCollection();
var registry = KafkaDocumentationRegistry.Register(services);
registry.Brokers.Add("default", "localhost:9092");
registry.Operations.Add(new KafkaOperationDescription(
    new ApiDocumentDescriptor("matchmaking-kafka-v1", "Matchmaking Kafka v1", "matchmaking", "1.0", ApiDocumentFormat.AsyncApi),
    typeof(MatchCreated), "matches", "default", AsyncApiAction.Send, null));
registry.Operations.Add(new KafkaOperationDescription(
    new ApiDocumentDescriptor("matchmaking-kafka-v1", "Matchmaking Kafka v1", "matchmaking", "1.0", ApiDocumentFormat.AsyncApi),
    typeof(MatchCreated), "matches", "default", AsyncApiAction.Receive, () => "matchmaking"));
services.AddApiDocumentationSchemas(SmokeJsonContext.Default);
using var provider = services.BuildServiceProvider();
var document = provider.GetServices<IApiDocumentSource>().OfType<IAsyncApiDocumentSource>().Single()
    .Generate("matchmaking-kafka-v1");
if (document["operations"]!.AsObject().Count != 2)
{
    throw new InvalidOperationException("Kafka metadata generation failed in Native AOT.");
}

Console.WriteLine(document.ToJsonString());
