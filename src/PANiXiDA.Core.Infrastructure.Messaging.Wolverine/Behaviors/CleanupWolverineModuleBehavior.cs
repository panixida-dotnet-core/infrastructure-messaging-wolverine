using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Behaviors;

/// <summary>
/// Cleans up the active transaction and releases the module associated with a mediator request.
/// </summary>
/// <typeparam name="TRequest">The request type processed by the pipeline.</typeparam>
/// <typeparam name="TResult">The request result type.</typeparam>
/// <param name="unitOfWork">The active module unit of work.</param>
/// <param name="serviceProvider">The current request service provider.</param>
public sealed class CleanupWolverineModuleBehavior<TRequest, TResult>(
    IUnitOfWork unitOfWork,
    IServiceProvider serviceProvider) : IFinallyRequestBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result
{
    /// <summary>
    /// Rolls back failed transactions, releases transaction resources, and deactivates the module.
    /// </summary>
    /// <param name="request">The request that was processed.</param>
    /// <param name="result">The request result, if one was produced.</param>
    /// <param name="exception">The exception thrown during request processing, if any.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous cleanup operation.</returns>
    public async Task FinallyAsync(
        TRequest request,
        TResult? result,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!unitOfWork.HasActiveTransaction)
            {
                return;
            }

            if (exception is not null || result is null || !result.IsSuccess)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
            }

            await unitOfWork.DisposeTransactionAsync();
        }
        finally
        {
            serviceProvider
                .GetRequiredService<WolverineModuleExecutionContext>()
                .Exit(typeof(TRequest));
        }
    }
}
