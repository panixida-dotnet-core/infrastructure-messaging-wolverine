using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Behaviors;

/// <summary>
/// Activates the module associated with the current mediator request.
/// </summary>
/// <typeparam name="TRequest">The request type processed by the pipeline.</typeparam>
/// <typeparam name="TResult">The request result type.</typeparam>
/// <param name="serviceProvider">The current request service provider.</param>
public sealed class ActivateWolverineModuleBehavior<TRequest, TResult>(
    IServiceProvider serviceProvider) : IBeforeRequestBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result
{
    /// <summary>
    /// Activates module-scoped persistence services for the current request.
    /// </summary>
    /// <param name="request">The request being processed.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A successful result that allows request processing to continue.</returns>
    public Task<Result> BeforeAsync(
        TRequest request,
        CancellationToken cancellationToken)
    {
        serviceProvider
            .GetRequiredService<WolverineModuleExecutionContext>()
            .Enter(typeof(TRequest));

        return Task.FromResult(Result.Success());
    }
}
