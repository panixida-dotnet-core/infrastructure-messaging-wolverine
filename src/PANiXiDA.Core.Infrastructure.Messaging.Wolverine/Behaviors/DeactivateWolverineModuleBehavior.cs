using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Behaviors;

/// <summary>
/// Releases module routing after a mediator request finishes.
/// </summary>
/// <typeparam name="TRequest">The request type processed by the pipeline.</typeparam>
/// <typeparam name="TResult">The request result type.</typeparam>
/// <param name="serviceProvider">The current request service provider.</param>
public sealed class DeactivateWolverineModuleBehavior<TRequest, TResult>(
    IServiceProvider serviceProvider) : IFinallyRequestBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result
{
    /// <summary>
    /// Releases the module associated with the completed request.
    /// </summary>
    /// <param name="request">The request that was processed.</param>
    /// <param name="result">The request result, if one was produced.</param>
    /// <param name="exception">The exception thrown during request processing, if any.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A completed task.</returns>
    public Task FinallyAsync(
        TRequest request,
        TResult? result,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        serviceProvider
            .GetRequiredService<WolverineModuleExecutionContext>()
            .Exit(typeof(TRequest));

        return Task.CompletedTask;
    }
}
