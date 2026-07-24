using System.Reflection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;

internal sealed class WolverineModuleRegistry(
    IReadOnlyList<WolverineModuleRegistration> registrations,
    IReadOnlyDictionary<Assembly, Type> assemblyOwners)
{
    internal IReadOnlyList<WolverineModuleRegistration> Registrations { get; } = registrations;

    internal IReadOnlyCollection<Assembly> ModuleAssemblies { get; } =
        [.. assemblyOwners.Keys];

    internal Type ResolveDbContextType(Type requestType)
    {
        if (assemblyOwners.TryGetValue(requestType.Assembly, out var dbContextType))
        {
            return dbContextType;
        }

        throw new InvalidOperationException(
            $"Request type '{requestType.FullName}' does not belong to a registered Wolverine module. " +
            "Register its assembly with WolverineModuleConfiguration.AddModule<TDbContext>().");
    }

    internal bool Owns(Type messageType)
    {
        return assemblyOwners.ContainsKey(messageType.Assembly);
    }
}
