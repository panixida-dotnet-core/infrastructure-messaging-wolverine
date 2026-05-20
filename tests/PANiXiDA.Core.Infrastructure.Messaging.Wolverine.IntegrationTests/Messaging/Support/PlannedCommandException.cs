using System.Diagnostics.CodeAnalysis;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Support;

[SuppressMessage("Major Code Smell", "S2094:Classes should not be empty", Justification = "Typed exception marker used to assert planned integration rollback scenarios.")]
public sealed class PlannedCommandException() : Exception("The integration command failed as planned.");
