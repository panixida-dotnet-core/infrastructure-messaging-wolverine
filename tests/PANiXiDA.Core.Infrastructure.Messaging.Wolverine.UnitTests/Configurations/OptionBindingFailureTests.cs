using System.ComponentModel;
using System.Text.Json;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class OptionBindingFailureTests
{
    [Theory(DisplayName = "Generated constructor binding supplies absent and present configuration sections")]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("scalar")]
    [InlineData("children")]
    public void BinderShouldBindSectionParameters(string kind)
    {
        var configuration = new ConfigurationManager { ["Options:Value:Name"] = "configured" };
        if (kind != "missing")
        {
            configuration[kind == "children" ? "Options:Value:Section:Child" : "Options:Value:Section"] =
                kind == "null" ? null : "configured";
        }

        var original = configuration.GetSection("Options").Get<HolderOptions<SectionPayload>>()!;
        var generated = OptionBindingRegistry.GetRequired<HolderOptions<SectionPayload>>(configuration, "Options");
        var originalOptional = configuration.GetSection("Options").Get<HolderOptions<OptionalSectionPayload>>()!;
        var generatedOptional = OptionBindingRegistry.GetRequired<HolderOptions<OptionalSectionPayload>>(configuration, "Options");

        generated.Value.ShouldNotBeNull();
        generated.Value.Section.Path.ShouldBe(original.Value!.Section.Path);
        generated.Value.Section.Path.ShouldBe("Options:Value:section");
        generated.Value.Section.Value.ShouldBe(original.Value.Section.Value);
        generated.Value.Section["Child"].ShouldBe(original.Value.Section["Child"]);
        generatedOptional.Value.ShouldNotBeNull();
        generatedOptional.Value.Section.ShouldNotBeNull();
        generatedOptional.Value.Section.Path.ShouldBe(originalOptional.Value!.Section!.Path);
        generatedOptional.Value.Section.Value.ShouldBe(originalOptional.Value.Section.Value);
        generatedOptional.Value.Section["Child"].ShouldBe(originalOptional.Value.Section["Child"]);
    }

    [Theory(DisplayName = "Generated binding preserves custom converter constructor exception chains")]
    [InlineData("root")]
    [InlineData("property")]
    [InlineData("parameter")]
    [InlineData("typed-root")]
    [InlineData("typed-property")]
    [InlineData("typed-parameter")]
    public void BinderShouldPreserveConverterConstructionErrors(string kind)
    {
        var configuration = new ConfigurationManager
        {
            ["Options"] = "configured",
            ["Options:value"] = "configured",
        };

        switch (kind)
        {
            case "root": AssertSameFailure<ThrowingConverterOptions>(configuration); break;
            case "property": AssertSameFailure<HolderOptions<ThrowingConverterOptions>>(configuration); break;
            case "parameter": AssertSameFailure<ParameterOptions<ThrowingConverterOptions>>(configuration); break;
            case "typed-root": AssertSameFailure<ThrowingTypedConverterOptions>(configuration); break;
            case "typed-property": AssertSameFailure<HolderOptions<ThrowingTypedConverterOptions>>(configuration); break;
            case "typed-parameter": AssertSameFailure<ParameterOptions<ThrowingTypedConverterOptions>>(configuration); break;
        }
    }

    [Theory(DisplayName = "Generated constructor binding rejects unconvertible scalar parameters")]
    [InlineData("object", "invalid")]
    [InlineData("object", "")]
    [InlineData("array", "invalid")]
    [InlineData("array", "")]
    [InlineData("struct", "invalid")]
    [InlineData("struct", "")]
    [InlineData("nullable", "invalid")]
    [InlineData("nullable", "")]
    [InlineData("converter", "invalid")]
    public void BinderShouldRejectUnconvertibleParameters(string kind, string value)
    {
        var configuration = new ConfigurationManager { ["Options:value"] = value };

        switch (kind)
        {
            case "object": AssertSameFailure<ParameterOptions<Payload>>(configuration); break;
            case "array": AssertSameFailure<ParameterOptions<string[]>>(configuration); break;
            case "struct": AssertSameFailure<ParameterOptions<Point>>(configuration); break;
            case "nullable": AssertSameFailure<ParameterOptions<Point?>>(configuration); break;
            case "converter": AssertSameFailure<ParameterOptions<NonConvertingPayload>>(configuration); break;
        }
    }

    [Theory(DisplayName = "Generated binding preserves constructor and accessor exception chains")]
    [InlineData("parameterless")]
    [InlineData("parameterized")]
    [InlineData("nested")]
    [InlineData("required")]
    [InlineData("setter")]
    [InlineData("init")]
    [InlineData("constructor-getter")]
    [InlineData("constructor-setter")]
    public void BinderShouldPreserveInvocationErrors(string kind)
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Name"] = "configured",
            ["Options:Value:Name"] = "configured",
        };

        switch (kind)
        {
            case "parameterless": AssertSameFailure<ThrowingDefaultOptions>(configuration); break;
            case "parameterized": AssertSameFailure<ThrowingParameterOptions>(configuration); break;
            case "nested": AssertSameFailure<HolderOptions<ThrowingPayload>>(configuration); break;
            case "required": AssertSameFailure<HolderOptions<ThrowingRequiredPayload>>(configuration); break;
            case "setter": AssertSameFailure<ThrowingSetterOptions>(configuration); break;
            case "init": AssertSameFailure<ThrowingInitOptions>(configuration); break;
            case "constructor-getter": AssertSameFailure<ThrowingConstructorGetterOptions>(configuration); break;
            case "constructor-setter": AssertSameFailure<ThrowingConstructorSetterOptions>(configuration); break;
        }
    }

    [Fact(DisplayName = "Generated binding preserves supported constructor arguments and nullable defaults")]
    public void BinderShouldPreserveValidParameters()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:value:Name"] = "nested",
            ["Optional:value"] = "",
        };

        var generated = OptionBindingRegistry.GetRequired<ParameterOptions<Payload>>(configuration, "Options");
        var original = configuration.GetSection("Options").Get<ParameterOptions<Payload>>()!;
        generated.Value.Name.ShouldBe(original.Value.Name);
        var optional = OptionBindingRegistry.GetRequired<OptionalOptions>(configuration, "Optional");
        optional.Value.ShouldBe(configuration.GetSection("Optional").Get<OptionalOptions>()!.Value);
    }

    [Fact(DisplayName = "Generated binding ignores unsupported dictionary keys and set interfaces")]
    public void BinderShouldPreserveCollectionRestrictions()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Booleans:true"] = "configured",
            ["Options:Characters:a"] = "configured",
            ["Options:Decimals:1.5"] = "configured",
            ["Options:Dates:2026-01-01"] = "configured",
            ["Options:MissingDictionary:true"] = "configured",
            ["Options:ReadOnlyDictionary:true"] = "configured",
            ["Options:ConcreteDictionary:true"] = "configured",
            ["Options:Set:0"] = "2",
            ["Options:ReadOnlySet:0"] = "2",
            ["Options:MissingSet:0"] = "2",
            ["Options:MissingReadOnlySet:0"] = "2",
            ["Options:ConcreteSet:0"] = "2",
            ["Options:EnumSet:0"] = "Friday",
            ["Options:ReadOnlyEnumSet:0"] = "Friday",
        };

        var original = configuration.GetSection("Options").Get<RestrictedOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<RestrictedOptions>(configuration, "Options");

        JsonSerializer.Serialize(generated).ShouldBe(JsonSerializer.Serialize(original));
        generated.ConcreteSet.ShouldBe([1, 2], ignoreOrder: true);
        generated.ConcreteDictionary.ShouldNotBeNull();
        generated.MissingDictionary.ShouldBeNull();
        generated.MissingSet.ShouldBeNull();
    }

    [Fact(DisplayName = "Generated dictionaries preserve all supported integer key types")]
    public void BinderShouldPreserveIntegerDictionaryKeys()
    {
        var configuration = new ConfigurationManager();
        foreach (var property in new[] { "SByte", "Byte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64" })
        {
            configuration[$"Options:{property}:1"] = "2";
        }

        var original = configuration.GetSection("Options").Get<IntegerDictionaryOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<IntegerDictionaryOptions>(configuration, "Options");

        JsonSerializer.Serialize(generated).ShouldBe(JsonSerializer.Serialize(original));
    }

    private static void AssertSameFailure<T>(ConfigurationManager configuration) where T : class
    {
        var original = Record.Exception(() => configuration.GetSection("Options").Get<T>());
        var generated = Record.Exception(() => OptionBindingRegistry.GetRequired<T>(configuration, "Options"));

        original.ShouldNotBeNull("The original binder must reject this input.");
        generated.ShouldNotBeNull("The generated binder must reject this input.");
        ExceptionTypes(generated).ShouldBe(ExceptionTypes(original));
    }

    private static IEnumerable<Type> ExceptionTypes(Exception? error)
    {
        while (error is not null)
        {
            yield return error.GetType();
            error = error.InnerException;
        }
    }

    public sealed class ParameterOptions<T>(T value) : KafkaConsumerOption
    {
        public T Value { get; } = value;
    }

    public sealed class HolderOptions<T> : KafkaConsumerOption
    {
        public T? Value { get; init; }
    }

    public sealed class OptionalOptions(int? value = 7) : KafkaConsumerOption
    {
        public int? Value { get; } = value;
    }

    public sealed class Payload
    {
        public string Name { get; init; } = "initial";
    }

    public sealed class SectionPayload(IConfigurationSection section)
    {
        public IConfigurationSection Section { get; } = section;
        public string? Name { get; init; }
    }

    public sealed class OptionalSectionPayload(IConfigurationSection? section = null)
    {
        public IConfigurationSection? Section { get; } = section;
        public string? Name { get; init; }
    }

    [TypeConverter(typeof(ThrowingConverter))]
    public sealed class ThrowingConverterOptions : KafkaConsumerOption;

    [TypeConverter(typeof(ThrowingTypedConverter))]
    public sealed class ThrowingTypedConverterOptions : KafkaConsumerOption;

    public sealed class ThrowingConverter : TypeConverter
    {
        public ThrowingConverter() => throw new FormatException("Converter constructor failed.");
    }

    public sealed class ThrowingTypedConverter : TypeConverter
    {
        public ThrowingTypedConverter(Type type) => throw new FormatException(type.Name);
    }

    public readonly record struct Point(int X);

    [TypeConverter(typeof(TypeConverter))]
    public sealed class NonConvertingPayload;

    public sealed class ThrowingDefaultOptions : KafkaConsumerOption
    {
        public ThrowingDefaultOptions() => throw new FormatException("Constructor failed.");
    }

    public sealed class ThrowingParameterOptions : KafkaConsumerOption
    {
        public ThrowingParameterOptions(string name) => throw new FormatException(name);
        public string Name { get; } = "initial";
    }

    public sealed class ThrowingPayload
    {
        public ThrowingPayload(string name) => throw new FormatException(name);
        public string Name { get; } = "initial";
    }

    public sealed class ThrowingRequiredPayload
    {
        public ThrowingRequiredPayload() => throw new FormatException("Constructor failed.");
        public required string Name { get; init; }
    }

    public sealed class ThrowingSetterOptions : KafkaConsumerOption
    {
        public string Name { get => TopicName; set => throw new FormatException(value); }
    }

    public sealed class ThrowingConstructorGetterOptions(string Name) : KafkaConsumerOption
    {
        private readonly string message = Name;
        public string Name { get => throw new FormatException(message); set => TopicName = value; }
    }

    public sealed class ThrowingConstructorSetterOptions(string Name) : KafkaConsumerOption
    {
        private readonly string name = Name;
        public string Name { get => name; set => throw new FormatException(value); }
    }

    public sealed class ThrowingInitOptions : KafkaConsumerOption
    {
        public string Name { get => TopicName; init => throw new FormatException(value); }
    }

    public sealed class RestrictedOptions : KafkaConsumerOption
    {
        public Dictionary<bool, string> Booleans { get; init; } = new() { [false] = "initial" };
        public Dictionary<char, string> Characters { get; init; } = [];
        public Dictionary<decimal, string> Decimals { get; init; } = [];
        public Dictionary<DateTime, string> Dates { get; init; } = [];
        public IDictionary<bool, string>? MissingDictionary { get; init; }
        public IReadOnlyDictionary<bool, string> ReadOnlyDictionary { get; init; } = new Dictionary<bool, string> { [false] = "initial" };
        public Dictionary<bool, string>? ConcreteDictionary { get; init; }
        public ISet<int> Set { get; init; } = new HashSet<int> { 1 };
        public IReadOnlySet<int> ReadOnlySet { get; init; } = new HashSet<int> { 1 };
        public ISet<int>? MissingSet { get; init; }
        public IReadOnlySet<int>? MissingReadOnlySet { get; init; }
        public HashSet<int> ConcreteSet { get; init; } = [1];
        public ISet<DayOfWeek> EnumSet { get; init; } = new HashSet<DayOfWeek> { DayOfWeek.Monday };
        public IReadOnlySet<DayOfWeek> ReadOnlyEnumSet { get; init; } = new HashSet<DayOfWeek> { DayOfWeek.Monday };
    }

    public sealed class IntegerDictionaryOptions : KafkaConsumerOption
    {
        public Dictionary<sbyte, int> SByte { get; init; } = [];
        public Dictionary<byte, int> Byte { get; init; } = [];
        public Dictionary<short, int> Int16 { get; init; } = [];
        public Dictionary<ushort, int> UInt16 { get; init; } = [];
        public Dictionary<int, int> Int32 { get; init; } = [];
        public Dictionary<uint, int> UInt32 { get; init; } = [];
        public Dictionary<long, int> Int64 { get; init; } = [];
        public Dictionary<ulong, int> UInt64 { get; init; } = [];
    }
}
