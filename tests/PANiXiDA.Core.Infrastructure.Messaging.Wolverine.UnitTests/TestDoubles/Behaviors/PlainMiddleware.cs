namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class PlainMiddleware : IDisposable
{
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
