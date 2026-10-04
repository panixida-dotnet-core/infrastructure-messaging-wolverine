using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Configurations;

public sealed class TestRecurringCommandOption : RecurringCommandOption
{
    public TestRecurringCommandOption()
    {
        Name = "test-command";
    }
}
