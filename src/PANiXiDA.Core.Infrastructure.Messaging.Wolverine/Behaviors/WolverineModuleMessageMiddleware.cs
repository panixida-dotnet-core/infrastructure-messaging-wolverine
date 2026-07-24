using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;

using Wolverine;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Behaviors;

/// <summary>
/// Activates module routing for Wolverine messages that do not use the PANiXiDA request pipeline.
/// </summary>
public sealed class WolverineModuleMessageMiddleware
{
    /// <summary>
    /// Activates the module that owns the incoming message type.
    /// </summary>
    /// <param name="envelope">The incoming Wolverine envelope.</param>
    /// <param name="serviceProvider">The current message service provider.</param>
    public static void Before(
        Envelope envelope,
        IServiceProvider serviceProvider)
    {
        serviceProvider
            .GetRequiredService<WolverineModuleExecutionContext>()
            .Enter(envelope.Message!.GetType());
    }

    /// <summary>
    /// Releases the module that owns the completed message type.
    /// </summary>
    /// <param name="envelope">The completed Wolverine envelope.</param>
    /// <param name="serviceProvider">The current message service provider.</param>
    public static void Finally(
        Envelope envelope,
        IServiceProvider serviceProvider)
    {
        serviceProvider
            .GetRequiredService<WolverineModuleExecutionContext>()
            .Exit(envelope.Message!.GetType());
    }
}
