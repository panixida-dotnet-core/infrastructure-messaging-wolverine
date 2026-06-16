using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Policies.Core;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;

/// <summary>
/// Configures PANiXiDA request behaviors applied to Wolverine request handlers.
/// </summary>
public sealed class WolverineRequestBehaviorConfiguration
{
    internal WolverineRequestBehaviorConfiguration()
    {
        Before = new WolverineRequestBehaviorStageConfiguration(
            typeof(IBeforeRequestBehavior<,>),
            "Before");

        After = new WolverineRequestBehaviorStageConfiguration(
            typeof(IAfterRequestBehavior<,>),
            "After");

        Finally = new WolverineRequestBehaviorStageConfiguration(
            typeof(IFinallyRequestBehavior<,>),
            "Finally");
    }

    /// <summary>
    /// Gets the configuration for behaviors executed before request handlers.
    /// </summary>
    public WolverineRequestBehaviorStageConfiguration Before { get; }

    /// <summary>
    /// Gets the configuration for behaviors executed after successful handler execution.
    /// </summary>
    public WolverineRequestBehaviorStageConfiguration After { get; }

    /// <summary>
    /// Gets the configuration for behaviors executed when request processing completes or fails.
    /// </summary>
    public WolverineRequestBehaviorStageConfiguration Finally { get; }

    internal static WolverineRequestBehaviorConfiguration CreateDefault()
    {
        var configuration = new WolverineRequestBehaviorConfiguration();

        configuration.Before.Add(typeof(ValidationBehavior<,>));
        configuration.Before.Add(typeof(BeginTransactionBehavior<,>));

        configuration.After.Add(typeof(PublishDomainEventsBehavior<,>));
        configuration.After.Add(typeof(CommitTransactionBehavior<,>));
        configuration.After.Add(typeof(FlushOutgoingMessagesBehavior<,>));

        configuration.Finally.Add(typeof(CleanupTransactionBehavior<,>));

        return configuration;
    }

    internal RequestMiddlewareRegistry Build()
    {
        return new RequestMiddlewareRegistry(
            Before.Build(),
            After.Build(),
            Finally.Build());
    }
}
