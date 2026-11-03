using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Events;

using Wolverine.Attributes;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Handlers;

[MaximumAttempts(1)]
public static class CascadingEventHandler
{
    public static PipelineEvent Handle(CascadingEvent @event)
    {
        return new PipelineEvent(@event.EventId);
    }
}

[MaximumAttempts(1)]
public static class AsyncCascadingEventHandler
{
    public static Task<PipelineEvent> HandleAsync(AsyncCascadingEvent @event)
    {
        return Task.FromResult(new PipelineEvent(@event.EventId));
    }
}

[MaximumAttempts(1)]
public static class ValueTaskCascadingEventHandler
{
    public static ValueTask<PipelineEvent> HandleAsync(ValueTaskCascadingEvent @event)
    {
        return ValueTask.FromResult(new PipelineEvent(@event.EventId));
    }
}
