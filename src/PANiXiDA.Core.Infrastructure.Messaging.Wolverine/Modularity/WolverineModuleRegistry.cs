using System.Reflection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Modularity;

internal sealed class WolverineModuleRegistry(
    IReadOnlyList<WolverineModuleRegistration> registrations,
    IReadOnlyDictionary<Assembly, Type> requestAssemblyOwners)
{
    internal IReadOnlyList<WolverineModuleRegistration> Registrations { get; } = registrations;

    internal IReadOnlyCollection<Assembly> DiscoveryAssemblies { get; } =
        [.. registrations
            .SelectMany(registration => registration.DiscoveryAssemblies)
            .Distinct()];

    internal Type ResolveDbContextType(Type requestType)
    {
        if (requestAssemblyOwners.TryGetValue(
                requestType.Assembly,
                out var dbContextType))
        {
            return dbContextType;
        }

        throw new InvalidOperationException(
            $"Request type '{requestType.FullName}' does not belong to a registered Wolverine module. " +
            "Register its assembly with WolverineModuleConfiguration.AddModule<TDbContext>().");
    }
}
