using System.Reflection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;

internal sealed record WolverineModuleRegistration(
    Type DbContextType,
    Assembly RequestAssembly,
    IReadOnlyList<Assembly> DiscoveryAssemblies);
