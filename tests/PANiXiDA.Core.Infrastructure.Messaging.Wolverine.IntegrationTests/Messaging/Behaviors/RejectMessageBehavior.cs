using PANiXiDA.Core.Domain.Abstractions;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Behaviors;

public sealed class RejectMessageBehavior<TMessage> : IBeforeMessageBehavior<TMessage>
    where TMessage : IMessage
{
    public Task<Result> BeforeAsync(TMessage message, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Failure(Error.Validation("Rejected before handler.")));
    }
}
