using System.Diagnostics.CodeAnalysis;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

[SuppressMessage("Major Code Smell", "S3453:Classes should not have only private constructors", Justification = "Used to verify middleware validation rejects types without a public constructor.")]
public sealed class BehaviorWithoutPublicConstructor
{
    private BehaviorWithoutPublicConstructor()
    {
    }
}
