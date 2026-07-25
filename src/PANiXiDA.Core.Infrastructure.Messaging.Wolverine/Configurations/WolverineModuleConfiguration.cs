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
    /// Registers a module DbContext, its request assembly, and optional additional handler assemblies.
    /// </summary>
    /// <typeparam name="TDbContext">The module write DbContext type.</typeparam>
    /// <param name="requestAssembly">The assembly that contains requests owned by the module.</param>
    /// <param name="handlerAssemblies">Additional assemblies where Wolverine should discover module handlers.</param>
    /// <returns>The same configuration instance for fluent configuration.</returns>
    public WolverineModuleConfiguration AddModule<TDbContext>(
        Assembly requestAssembly,
        params Assembly[] handlerAssemblies)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(requestAssembly);
        ArgumentNullException.ThrowIfNull(handlerAssemblies);

        if (handlerAssemblies.Any(assembly => assembly is null))
        {
            throw new ArgumentException(
                "Handler assemblies must not contain null values.",
                nameof(handlerAssemblies));
        }

        registrations.Add(new WolverineModuleRegistration(
            typeof(TDbContext),
            requestAssembly,
            [.. handlerAssemblies
                .Prepend(requestAssembly)
                .Distinct()]));

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

        var requestAssemblyOwners = new Dictionary<Assembly, Type>();

        foreach (var registration in registrations)
        {
            if (requestAssemblyOwners.TryGetValue(
                    registration.RequestAssembly,
                    out var existingOwner) &&
                existingOwner != registration.DbContextType)
            {
                throw new InvalidOperationException(
                    $"Request assembly '{registration.RequestAssembly.FullName}' is assigned to both " +
                    $"'{existingOwner.FullName}' and '{registration.DbContextType.FullName}'.");
            }

            requestAssemblyOwners[registration.RequestAssembly] =
                registration.DbContextType;
        }

        return new WolverineModuleRegistry(
            [.. registrations],
            requestAssemblyOwners);
    }
}
