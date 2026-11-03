using PANiXiDA.Core.Domain.Abstractions;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public sealed class MessageBeforeBehavior<TMessage> : IBeforeMessageBehavior<TMessage>
    where TMessage : IMessage
{
    public Task<Result> BeforeAsync(TMessage message, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success());
    }
}
