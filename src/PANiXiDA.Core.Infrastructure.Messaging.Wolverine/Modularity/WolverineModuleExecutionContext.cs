using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;

internal sealed class WolverineModuleExecutionContext(
    IServiceProvider serviceProvider,
    WolverineModuleRegistry moduleRegistry)
{
    private readonly Stack<ActiveModule> activeModules = [];

    internal void Enter(Type requestType)
    {
        var dbContextType = moduleRegistry.ResolveDbContextType(requestType);
        activeModules.Push(new ActiveModule(requestType, dbContextType));
    }

    internal void Exit(Type requestType)
    {
        if (activeModules.Count == 0)
        {
            throw new InvalidOperationException(
                $"Cannot leave Wolverine module for request '{requestType.FullName}' because no module is active.");
        }

        var activeModule = activeModules.Pop();

        if (activeModule.RequestType != requestType)
        {
            throw new InvalidOperationException(
                $"Cannot leave Wolverine module for request '{requestType.FullName}' while " +
                $"'{activeModule.RequestType.FullName}' is active.");
        }
    }

    internal IUnitOfWork GetUnitOfWork()
    {
        var dbContextType = GetActiveModule().DbContextType;

        return serviceProvider.GetKeyedService<IUnitOfWork>(dbContextType)
            ?? throw new InvalidOperationException(
                $"No keyed IUnitOfWork is registered for DbContext '{dbContextType.FullName}'. " +
                "Register the module write DbContext with PANiXiDA EF persistence infrastructure.");
    }

    internal IOutboxDispatcher GetOutboxDispatcher()
    {
        var dbContextType = GetActiveModule().DbContextType;

        return serviceProvider.GetKeyedService<IOutboxDispatcher>(dbContextType)
            ?? throw new InvalidOperationException(
                $"No Wolverine outbox dispatcher is registered for DbContext '{dbContextType.FullName}'.");
    }

    private ActiveModule GetActiveModule()
    {
        if (activeModules.TryPeek(out var activeModule))
        {
            return activeModule;
        }

        throw new InvalidOperationException(
            "No Wolverine module is active in the current request scope. " +
            "Use the keyed module service when publishing or managing transactions outside the mediator pipeline.");
    }

    private sealed record ActiveModule(
        Type RequestType,
        Type DbContextType);
}
