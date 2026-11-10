using System.Text.Json.Serialization;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.AotSmoke;

internal sealed record MatchCreated(Guid MatchId);

[JsonSerializable(typeof(MatchCreated))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class SmokeJsonContext : JsonSerializerContext;
