using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

using Microsoft.Extensions.DependencyInjection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;

/// <summary>
/// Stores validator registrations emitted by the package's source generator.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ValidatorRegistry
{
    private static readonly ConditionalWeakTable<Assembly,
        ConcurrentDictionary<string, ConcurrentDictionary<string, Registration>>> assemblies = [];

    /// <summary>
    /// Records assemblies inspected by a generated module, including assemblies without validators.
    /// </summary>
    public static void RegisterAssemblies(
        Assembly generatedAssembly,
        params string[] assemblyNames)
    {
        ArgumentNullException.ThrowIfNull(generatedAssembly);
        ArgumentNullException.ThrowIfNull(assemblyNames);
        var metadata = assemblies.GetValue(generatedAssembly, static _ => new(StringComparer.Ordinal));
        foreach (var assemblyName in assemblyNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
            metadata.GetOrAdd(assemblyName, static _ => new(StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// Registers a validator's DI configuration, or records an inaccessible validator with a null callback.
    /// Accessible registrations from another generated module satisfy inaccessible entries.
    /// </summary>
    public static void Register(
        Assembly generatedAssembly,
        string assemblyName,
        string validatorName,
        Action<IServiceCollection>? configure)
    {
        ArgumentNullException.ThrowIfNull(generatedAssembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(validatorName);

        var metadata = assemblies.GetValue(generatedAssembly, static _ => new(StringComparer.Ordinal));
        var registrations = metadata.GetOrAdd(assemblyName, static _ => new(StringComparer.Ordinal));
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

            var registrations = GetRegistrations(assembly);
            if (registrations is null)
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

    private static Dictionary<string, Registration>? GetRegistrations(Assembly assembly)
    {
        Dictionary<string, Registration>? registrations = null;
        foreach (var module in assemblies
                     .OrderBy(pair => pair.Key == assembly ? 0 : 1)
                     .ThenBy(pair => pair.Key.FullName, StringComparer.Ordinal))
        {
            if (!module.Value.TryGetValue(assembly.FullName!, out var metadata) ||
                !ReferencesAssembly(module.Key, assembly))
            {
                continue;
            }

            registrations ??= new(StringComparer.Ordinal);
            foreach (var entry in metadata)
            {
                if (!registrations.TryGetValue(entry.Key, out var existing) || existing.Configure is null)
                {
                    registrations[entry.Key] = entry.Value;
                }
            }
        }

        return registrations;
    }

    private static bool ReferencesAssembly(
        Assembly generatedAssembly,
        Assembly assembly)
    {
        if (generatedAssembly == assembly)
        {
            return true;
        }

        try
        {
            return AssemblyLoadContext.GetLoadContext(generatedAssembly)!
                .LoadFromAssemblyName(assembly.GetName()) == assembly;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (FileLoadException)
        {
            return false;
        }
    }

    private sealed record Registration(Action<IServiceCollection>? Configure);
}
