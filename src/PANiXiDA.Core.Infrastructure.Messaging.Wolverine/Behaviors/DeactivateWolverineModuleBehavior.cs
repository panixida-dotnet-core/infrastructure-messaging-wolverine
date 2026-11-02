using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Behaviors;

/// <summary>
/// Releases module routing after a mediator message finishes.
/// </summary>
/// <typeparam name="TMessage">The message type processed by the pipeline.</typeparam>
/// <typeparam name="TResult">The message result type.</typeparam>
/// <param name="serviceProvider">The current message service provider.</param>
public sealed class DeactivateWolverineModuleBehavior<TMessage, TResult>(
    IServiceProvider serviceProvider) : IFinallyMessageBehavior<TMessage, TResult>
    where TMessage : IRequest<TResult>
    where TResult : Result
{
    /// <summary>
    /// Releases the module associated with the completed message.
    /// </summary>
    /// <param name="message">The message that was processed.</param>
    /// <param name="result">The message result, if one was produced.</param>
    /// <param name="exception">The exception thrown during message processing, if any.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The supplied result unchanged after releasing module routing.</returns>
    public Task<Result?> FinallyAsync(
        TMessage message,
        TResult? result,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        serviceProvider
            .GetRequiredService<WolverineModuleExecutionContext>()
            .Exit(typeof(TMessage));

        return Task.FromResult<Result?>(result);
    }
}
