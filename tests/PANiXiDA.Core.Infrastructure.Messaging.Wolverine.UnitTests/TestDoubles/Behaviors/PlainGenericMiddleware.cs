namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class PlainGenericMiddleware<TRequest, TResult, THandler> : IDisposable
{
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
