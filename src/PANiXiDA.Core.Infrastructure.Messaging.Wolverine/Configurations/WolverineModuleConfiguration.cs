using Microsoft.EntityFrameworkCore;

using System.Reflection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;

/// <summary>
/// Configures module boundaries for a single Wolverine runtime.
/// </summary>
public sealed class WolverineModuleConfiguration
{
    private readonly List<WolverineModuleRegistration> registrations = [];

    /// <summary>
    /// Registers a module DbContext and the assemblies that contain its requests and handlers.
    /// </summary>
    /// <typeparam name="TDbContext">The module write DbContext type.</typeparam>
    /// <param name="moduleAssemblies">The assemblies owned by the module.</param>
    /// <returns>The same configuration instance for fluent configuration.</returns>
    public WolverineModuleConfiguration AddModule<TDbContext>(
        params Assembly[] moduleAssemblies)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(moduleAssemblies);

        if (moduleAssemblies.Length == 0)
        {
            throw new ArgumentException(
                "At least one module assembly must be specified.",
                nameof(moduleAssemblies));
        }

        if (moduleAssemblies.Any(assembly => assembly is null))
        {
            throw new ArgumentException(
                "Module assemblies must not contain null values.",
                nameof(moduleAssemblies));
        }

        registrations.Add(new WolverineModuleRegistration(
            typeof(TDbContext),
            [.. moduleAssemblies.Distinct()]));

        return this;
    }

    internal WolverineModuleRegistry Build()
    {
        if (registrations.Count == 0)
        {
            throw new InvalidOperationException(
                "At least one Wolverine module must be registered.");
        }

        var duplicateDbContext = registrations
            .GroupBy(registration => registration.DbContextType)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateDbContext is not null)
        {
            throw new InvalidOperationException(
                $"DbContext '{duplicateDbContext.Key.FullName}' is registered for more than one Wolverine module.");
        }

        var assemblyOwners = new Dictionary<Assembly, Type>();

        foreach (var registration in registrations)
        {
            foreach (var assembly in registration.ModuleAssemblies)
            {
                if (assemblyOwners.TryGetValue(assembly, out var existingOwner) &&
                    existingOwner != registration.DbContextType)
                {
                    throw new InvalidOperationException(
                        $"Assembly '{assembly.FullName}' is assigned to both " +
                        $"'{existingOwner.FullName}' and '{registration.DbContextType.FullName}'.");
                }

                assemblyOwners[assembly] = registration.DbContextType;
            }
        }

        return new WolverineModuleRegistry(
            [.. registrations],
            assemblyOwners);
    }
}
