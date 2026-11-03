using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Application.Messaging.Mediator.Handlers;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Behaviors;

/// <summary>
/// Activates the module associated with the current mediator message.
/// </summary>
/// <typeparam name="TMessage">The message type used to select the owning module.</typeparam>
/// <typeparam name="TResult">The result type returned by the message.</typeparam>
/// <typeparam name="THandler">The handler type processing the message.</typeparam>
/// <param name="serviceProvider">The current message service provider.</param>
public sealed class ActivateWolverineModuleBehavior<TMessage, TResult, THandler>(
    IServiceProvider serviceProvider) : IBeforeMessageBehavior<TMessage>
    where TMessage : IRequest<TResult>
    where TResult : Result
    where THandler : IRequestHandler<TMessage, TResult>
{
    /// <summary>
    /// Activates module-scoped persistence services for the current message.
    /// </summary>
    /// <param name="message">The message being processed.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A successful result that allows message processing to continue.</returns>
    public Task<Result> BeforeAsync(
        TMessage message,
        CancellationToken cancellationToken)
    {
        serviceProvider
            .GetRequiredService<WolverineModuleExecutionContext>()
            .Enter(typeof(TMessage));

        return Task.FromResult(Result.Success());
    }
}
