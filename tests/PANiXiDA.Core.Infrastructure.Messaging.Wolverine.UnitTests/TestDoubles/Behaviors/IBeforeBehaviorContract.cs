namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.Behaviors;

public interface IBeforeBehaviorContract<TRequest, TResult, THandler> : IBeforeRequestBehavior<TRequest, TResult, THandler>
    where TRequest : IRequest<TResult>
    where THandler : IRequestHandler<TRequest, TResult>
    where TResult : Result;
