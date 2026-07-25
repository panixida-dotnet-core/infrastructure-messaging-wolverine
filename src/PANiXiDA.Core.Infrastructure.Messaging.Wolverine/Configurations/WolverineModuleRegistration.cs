using System.Reflection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;

internal sealed record WolverineModuleRegistration(
    Type DbContextType,
    Assembly RequestAssembly,
    IReadOnlyList<Assembly> DiscoveryAssemblies);
