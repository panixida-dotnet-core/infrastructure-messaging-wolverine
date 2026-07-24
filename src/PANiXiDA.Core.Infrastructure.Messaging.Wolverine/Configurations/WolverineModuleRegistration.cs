using System.Reflection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;

internal sealed record WolverineModuleRegistration(
    Type DbContextType,
    IReadOnlyList<Assembly> ModuleAssemblies);
