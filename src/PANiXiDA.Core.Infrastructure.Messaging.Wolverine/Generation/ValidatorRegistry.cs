using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.DependencyInjection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;

/// <summary>
/// Stores validator registrations emitted by the package's source generator.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ValidatorRegistry
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Registration>> assemblies =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Records assemblies inspected at compile time, including assemblies without validators.
    /// </summary>
    public static void RegisterAssemblies(params string[] assemblyNames)
    {
        ArgumentNullException.ThrowIfNull(assemblyNames);
        foreach (var assemblyName in assemblyNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
            assemblies.GetOrAdd(assemblyName, static _ => new(StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// Registers a validator's DI configuration, or records an inaccessible validator with a null callback.
    /// Accessible registrations from another generated module satisfy inaccessible entries.
    /// </summary>
    public static void Register(
        string assemblyName,
        string validatorName,
        Action<IServiceCollection>? configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(validatorName);

        var registrations = assemblies.GetOrAdd(assemblyName, static _ => new(StringComparer.Ordinal));
        var registration = new Registration(configure);
        registrations.AddOrUpdate(
            validatorName,
            registration,
            (_, existing) => existing.Configure is null ? registration : existing);
    }

    internal static void AddValidators(
        IServiceCollection services,
        IEnumerable<Assembly> discoveryAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(discoveryAssemblies);

        foreach (var assembly in discoveryAssemblies.Distinct())
        {
            ArgumentNullException.ThrowIfNull(assembly);
            RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);

            if (!assemblies.TryGetValue(assembly.FullName!, out var registrations))
            {
                throw new InvalidOperationException(
                    $"No generated validator metadata exists for assembly '{assembly.FullName}'. " +
                    "Ensure the Wolverine package source generator is enabled in the host or declaring project and can see the discovery assembly.");
            }

            foreach (var entry in registrations.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                var configure = entry.Value.Configure ?? throw new InvalidOperationException(
                    $"No generated validator registration exists for '{entry.Key}' in assembly '{assembly.FullName}'. " +
                    "Make the validator and its validated type accessible to the host, or enable the Wolverine package source generator in the declaring project.");
                configure(services);
            }
        }
    }

    private sealed record Registration(Action<IServiceCollection>? Configure);
}
