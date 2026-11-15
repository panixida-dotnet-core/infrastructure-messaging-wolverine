using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Configuration;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;

/// <summary>
/// Stores configuration binders emitted by the Wolverine source generator.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class OptionBindingRegistry
{
    private static readonly ConditionalWeakTable<Type, TypeConverter> Converters = [];

    /// <summary>
    /// Registers a statically compiled binder for an options type.
    /// </summary>
    /// <typeparam name="TOption">The concrete options type.</typeparam>
    /// <param name="binder">Creates and binds an options instance from a nonempty section.</param>
    /// <param name="owner">The assembly that contains the generated binder.</param>
    public static void Register<TOption>(Func<IConfigurationSection, TOption> binder, Assembly owner)
        where TOption : class
    {
        ArgumentNullException.ThrowIfNull(binder);
        ArgumentNullException.ThrowIfNull(owner);
        Binding<TOption>.RegisterBinder(binder, owner);
    }

    /// <summary>
    /// Gets the cached type converter using a statically compiled factory, retrying failed construction.
    /// </summary>
    public static TypeConverter GetConverter<TValue>(Func<TypeConverter> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var type = typeof(TValue);
        return Converters.TryGetValue(type, out var converter) ? converter : Converters.GetValue(type,
            _ => factory() ?? throw new InvalidOperationException("The converter factory returned null."));
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

        RuntimeHelpers.RunModuleConstructor(typeof(TOption).Assembly.ManifestModule.ModuleHandle);
        var binder = Binding<TOption>.Binder ?? InitializeGeneratedBinder<TOption>()
            ?? throw new InvalidOperationException(
                $"No generated configuration binder exists for '{typeof(TOption).FullName}'. " +
                "Ensure the Wolverine source generator is enabled in the project declaring or registering the options.");
        return binder(section) ?? throw new InvalidOperationException(
            $"Configuration section '{sectionName}' could not be bound to '{typeof(TOption).FullName}'.");
    }

    private static Func<IConfigurationSection, TOption>? InitializeGeneratedBinder<TOption>()
        where TOption : class
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.CustomAttributes.Any(attribute =>
                attribute.AttributeType.BaseType == typeof(OptionBindingInitializerAttribute) &&
                attribute.ConstructorArguments.Any(argument => typeof(TOption).Equals(argument.Value))))
            {
                continue;
            }

            assembly.GetCustomAttributes<OptionBindingInitializerAttribute>()
                .First(initializer => initializer.OptionType == typeof(TOption)).RegisterBindings();
            return Binding<TOption>.Binder;
        }

        return null;
    }

    private static class Binding<TOption>
        where TOption : class
    {
        private static Func<IConfigurationSection, TOption>? binder;

        internal static Func<IConfigurationSection, TOption>? Binder
            => Volatile.Read(ref binder) ?? Collectible.Binders.FirstOrDefault().Value;

        internal static void RegisterBinder(Func<IConfigurationSection, TOption> generatedBinder, Assembly owner)
        {
            if (owner.IsCollectible)
            {
                Collectible.Binders.TryAdd(owner, generatedBinder);
                return;
            }

            Interlocked.CompareExchange(ref binder, generatedBinder, null);
        }

        private static class Collectible
        {
            internal static readonly ConditionalWeakTable<Assembly, Func<IConfigurationSection, TOption>> Binders = [];
        }
    }
}
