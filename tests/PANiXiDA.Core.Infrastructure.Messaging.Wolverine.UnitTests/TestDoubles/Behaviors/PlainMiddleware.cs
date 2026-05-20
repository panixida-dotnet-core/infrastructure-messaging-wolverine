using System.Diagnostics.CodeAnalysis;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

[SuppressMessage("Major Code Smell", "S2094:Classes should not be empty", Justification = "Used to verify middleware validation rejects classes without behavior contracts.")]
public sealed class PlainMiddleware;
