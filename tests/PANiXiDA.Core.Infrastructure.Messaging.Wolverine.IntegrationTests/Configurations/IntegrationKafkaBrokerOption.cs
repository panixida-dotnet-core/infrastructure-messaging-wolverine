using System.Diagnostics.CodeAnalysis;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Configurations;

[SuppressMessage("Major Code Smell", "S2094:Classes should not be empty", Justification = "Typed Kafka option model used to bind a named configuration section.")]
public sealed class IntegrationKafkaBrokerOption : KafkaBrokerOption;
