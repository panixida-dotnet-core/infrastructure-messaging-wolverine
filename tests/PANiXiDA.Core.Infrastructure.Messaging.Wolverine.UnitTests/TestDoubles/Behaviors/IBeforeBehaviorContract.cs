namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public interface IBeforeBehaviorContract<TRequest, TResult, THandler> : IBeforeMessageBehavior<TRequest>
    where TRequest : IRequest<TResult>
    where THandler : IRequestHandler<TRequest, TResult>
    where TResult : Result;
