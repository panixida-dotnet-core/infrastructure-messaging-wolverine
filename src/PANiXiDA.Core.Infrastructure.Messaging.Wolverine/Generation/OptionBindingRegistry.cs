using System.ComponentModel;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Configuration;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;

/// <summary>
/// Stores configuration binders emitted by the Wolverine source generator.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class OptionBindingRegistry
{
    /// <summary>
    /// Registers a statically compiled binder for an options type.
    /// </summary>
    /// <typeparam name="TOption">The concrete options type.</typeparam>
    /// <param name="binder">Creates and binds an options instance from a nonempty section.</param>
    public static void Register<TOption>(Func<IConfigurationSection, TOption> binder)
        where TOption : class
    {
        ArgumentNullException.ThrowIfNull(binder);
        Binding<TOption>.RegisterBinder(binder);
    }

    /// <summary>
    /// Converts a configuration value and preserves the configuration path in conversion errors.
    /// </summary>
    /// <typeparam name="TValue">The target value type.</typeparam>
    /// <param name="section">The configuration value being converted.</param>
    /// <param name="parse">The statically compiled conversion.</param>
    /// <returns>The converted value.</returns>
    public static TValue Parse<TValue>(IConfigurationSection section, Func<string, TValue> parse)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(parse);
        try
        {
            return parse(section.Value!);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Failed to convert configuration value '{section.Value}' at '{section.Path}' to type '{typeof(TValue)}'.", exception);
        }
    }

    internal static TOption GetRequired<TOption>(IConfiguration configuration, string sectionName)
        where TOption : class
    {
        var section = configuration.GetSection(sectionName);
        if (!section.Exists())
        {
            throw new InvalidOperationException($"Configuration section '{sectionName}' was not found.");
        }

        if (!section.GetChildren().Any())
        {
            throw new InvalidOperationException(
                $"Configuration section '{sectionName}' could not be bound to '{typeof(TOption).FullName}'.");
        }

        RuntimeHelpers.RunModuleConstructor(typeof(TOption).Assembly.ManifestModule.ModuleHandle);
        var binder = Binding<TOption>.Binder
            ?? throw new InvalidOperationException(
                $"No generated configuration binder exists for '{typeof(TOption).FullName}'. " +
                "Ensure the Wolverine source generator is enabled in the project declaring or registering the options.");
        return binder(section);
    }

    private static class Binding<TOption>
        where TOption : class
    {
        private static Func<IConfigurationSection, TOption>? binder;

        internal static Func<IConfigurationSection, TOption>? Binder => binder;

        internal static void RegisterBinder(Func<IConfigurationSection, TOption> generatedBinder)
        {
            Interlocked.CompareExchange(ref binder, generatedBinder, null);
        }
    }
}
