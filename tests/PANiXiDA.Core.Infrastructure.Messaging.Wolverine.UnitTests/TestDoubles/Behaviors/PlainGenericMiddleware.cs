using System.Diagnostics.CodeAnalysis;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

[SuppressMessage("Major Code Smell", "S2094:Classes should not be empty", Justification = "Used to verify middleware validation rejects open generic classes without behavior contracts.")]
[SuppressMessage("Major Code Smell", "S2326:Unused type parameters should be removed", Justification = "Used to verify middleware validation rejects open generic classes without behavior contracts.")]
public sealed class PlainGenericMiddleware<TRequest, TResult>;
