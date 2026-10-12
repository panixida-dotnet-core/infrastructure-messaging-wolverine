using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class OptionBindingCompatibilityTests
{
    [Theory(DisplayName = "Generated dictionary binding reads existing values only when needed")]
    [InlineData(false)]
    [InlineData(true)]
    public void BinderShouldDeferDictionaryLookups(bool hasChildren)
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Numbers:value"] = "42",
            ["Options:Numbers:invalid"] = "invalid",
            ["Options:Optional:value"] = "",
            ["Options:Converted:value"] = "configured",
            ["Options:Converted:invalid"] = "invalid",
            ["Options:Sections:value"] = "configured",
            ["Options:Lists:value:0"] = "8",
            ["Options:Bytes:value"] = "AgM=",
        };
        if (hasChildren)
        {
            foreach (var property in new[] { "Numbers", "Optional", "Converted", "Sections" })
            {
                configuration[$"Options:{property}:value:Nested"] = "ignored";
            }
        }

        var original = configuration.GetSection("Options").Get<DictionaryLookupOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<DictionaryLookupOptions>(configuration, "Options");

        original.Numbers["value"].ShouldBe(42);
        generated.Numbers.ShouldBe(original.Numbers);
        generated.Optional.ShouldBe(original.Optional);
        generated.Converted.ShouldBe(original.Converted);
        generated.Sections["value"].ShouldBeOfType(original.Sections["value"].GetType());
        generated.Sections["value"].Path.ShouldBe(original.Sections["value"].Path);
        generated.Sections["value"].Value.ShouldBe(original.Sections["value"].Value);
        generated.Numbers.Lookups.ShouldBe(0);
        generated.Optional.Lookups.ShouldBe(0);
        generated.Converted.Lookups.ShouldBe(0);
        generated.Sections.Lookups.ShouldBe(0);
        generated.Lists["value"].ShouldBe(original.Lists["value"]);
        generated.Lists["value"].ShouldBe([7, 8]);
        generated.Bytes["value"].ShouldBe(original.Bytes["value"]);
        generated.Bytes["value"].ShouldBe([1, 2, 3]);
        generated.Lists.Lookups.ShouldBe(original.Lists.Lookups);
        generated.Lists.Lookups.ShouldBe(1);
        generated.Bytes.Lookups.ShouldBe(original.Bytes.Lookups);
        generated.Bytes.Lookups.ShouldBe(1);
    }

    [Fact(DisplayName = "Generated binding reuses custom converters across probes, values and options types")]
    public void BinderShouldReuseConverterForConversion()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Value"] = "configured",
            ["Options:Values:0"] = "first",
            ["Options:Values:1"] = "second",
            ["Options:Entries:first"] = "first",
            ["Options:Entries:second"] = "second",
        };
        TypeDescriptor.Refresh(typeof(SingleConversionValue));
        SingleConversionConverter.Activations = 0;
        var original = new[]
        {
            configuration.GetSection("Options").Get<SingleConversionOptions>()!.Value!.Value.Name,
            configuration.GetSection("Options").Get<SingleConversionParameter>()!.Value.Name,
            string.Join(",", configuration.GetSection("Options").Get<SingleConversionArray>()!.Values.Select(value => value.Name)),
            string.Join(",", configuration.GetSection("Options").Get<SingleConversionDictionary>()!.Entries.Values.Select(value => value.Name)),
        };
        SingleConversionConverter.Activations.ShouldBe(1);
        SingleConversionConverter.Activations = 0;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var generated = new[]
            {
                OptionBindingRegistry.GetRequired<SingleConversionOptions>(configuration, "Options").Value!.Value.Name,
                OptionBindingRegistry.GetRequired<SingleConversionParameter>(configuration, "Options").Value.Name,
                string.Join(",", OptionBindingRegistry.GetRequired<SingleConversionArray>(configuration, "Options").Values.Select(value => value.Name)),
                string.Join(",", OptionBindingRegistry.GetRequired<SingleConversionDictionary>(configuration, "Options").Entries.Values.Select(value => value.Name)),
            };

            generated.ShouldBe(original);
        }
        SingleConversionConverter.Activations.ShouldBe(1);
    }

    [Theory(DisplayName = "Generated binding preserves intrinsic scalar and nullable conversions")]
    [InlineData("1.5")]
    [InlineData("")]
    [InlineData(null)]
    public void BinderShouldPreserveIntrinsicScalars(string? optional)
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Version"] = "1.2.3.4",
            ["Options:Half"] = "1.5",
            ["Options:Signed"] = "170141183460469231731687303715884105727",
            ["Options:Unsigned"] = "340282366920938463463374607431768211455",
            ["Options:Optional"] = optional,
            ["Options:Versions:0"] = "2.3",
            ["Options:Versions:1"] = "invalid",
        };

        var original = configuration.GetSection("Options").Get<IntrinsicOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<IntrinsicOptions>(configuration, "Options");

        generated.Version.ShouldBe(original.Version);
        generated.Half.ShouldBe(original.Half);
        generated.Signed.ShouldBe(original.Signed);
        generated.Unsigned.ShouldBe(original.Unsigned);
        generated.Optional.ShouldBe(original.Optional);
        generated.Versions.ShouldBe(original.Versions);
    }

    [Theory(DisplayName = "Generated binding preserves intrinsic scalar conversion errors")]
    [InlineData("Version")]
    [InlineData("Half")]
    [InlineData("Signed")]
    [InlineData("Unsigned")]
    public void BinderShouldPreserveIntrinsicScalarErrors(string property)
    {
        var configuration = new ConfigurationManager { [$"Options:{property}"] = "invalid" };

        var original = Should.Throw<InvalidOperationException>(() => configuration.GetSection("Options").Get<IntrinsicOptions>());
        var generated = Should.Throw<InvalidOperationException>(() =>
            OptionBindingRegistry.GetRequired<IntrinsicOptions>(configuration, "Options"));

        generated.Message.ShouldContain($"Options:{property}");
        generated.InnerException.ShouldBeOfType(original.InnerException!.GetType());
    }

    [Theory(DisplayName = "Generated binding accepts scalar option sections with a TypeConverter")]
    [InlineData("compact-value")]
    [InlineData("")]
    public void BinderShouldConvertScalarOptionSections(string value)
    {
        var configuration = new ConfigurationManager { ["Options"] = value };

        var original = configuration.GetSection("Options").Get<ScalarOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<ScalarOptions>(configuration, "Options");

        generated.TopicName.ShouldBe(original.TopicName);
    }

    [Fact(DisplayName = "Generated binding preserves root TypeConverter failures")]
    public void BinderShouldPreserveScalarOptionErrors()
    {
        var configuration = new ConfigurationManager { ["Options"] = "invalid" };

        var original = Should.Throw<InvalidOperationException>(() => configuration.GetSection("Options").Get<ScalarOptions>());
        var generated = Should.Throw<InvalidOperationException>(() =>
            OptionBindingRegistry.GetRequired<ScalarOptions>(configuration, "Options"));

        generated.Message.ShouldContain("Options");
        generated.InnerException.ShouldBeOfType(original.InnerException!.GetType());
    }

    [Fact(DisplayName = "Generated binding rejects null results from root TypeConverters")]
    public void BinderShouldRejectNullScalarOptions()
    {
        var configuration = new ConfigurationManager { ["Options"] = "null" };

        configuration.GetSection("Options").Get<ScalarOptions>().ShouldBeNull();
        var generated = Should.Throw<InvalidOperationException>(() =>
            OptionBindingRegistry.GetRequired<ScalarOptions>(configuration, "Options"));

        generated.Message.ShouldBe($"Configuration section 'Options' could not be bound to '{typeof(ScalarOptions).FullName}'.");
    }

    [Fact(DisplayName = "Generated binding preserves null byte arrays in collections and dictionaries")]
    public void BinderShouldPreserveNullByteArrayEntries()
    {
        using var json = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""
            { "Options": {
                "Values": [null, "AQI=", "", "invalid"],
                "Array": [null, "AQI=", ""],
                "Dictionary": { "null": null, "value": "AQI=", "empty": "" },
                "Empty": null,
                "Initialized": null
            } }
            """));
        var configuration = new ConfigurationManager();
        configuration.AddJsonStream(json);

        var original = configuration.GetSection("Options").Get<BinaryOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<BinaryOptions>(configuration, "Options");

        JsonSerializer.Serialize(generated).ShouldBe(JsonSerializer.Serialize(original));
        generated.Values.Count.ShouldBe(3);
        generated.Values[0].ShouldBeNull();
    }

    [Theory(DisplayName = "Generated binding preserves initialized byte arrays when binding Base64 scalars")]
    [InlineData("AgM=", false)]
    [InlineData("AgM=", true)]
    [InlineData("", false)]
    [InlineData("", true)]
    public void BinderShouldPreserveInitializedBinaryScalars(string value, bool hasChildren)
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Initialized"] = value,
            ["Options:Empty"] = value,
        };
        if (hasChildren)
        {
            configuration["Options:Initialized:0"] = "99";
        }

        var original = configuration.GetSection("Options").Get<BinaryOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<BinaryOptions>(configuration, "Options");

        generated.Initialized.ShouldBe(original.Initialized);
        generated.Initialized.ShouldBe(new byte[] { 1 }.Concat(Convert.FromBase64String(value)));
        generated.Empty.ShouldBe(original.Empty);
    }

    [Fact(DisplayName = "Generated binding appends array children to initialized byte arrays")]
    public void BinderShouldAppendBinaryChildren()
    {
        var configuration = new ConfigurationManager { ["Options:Initialized:0"] = "2", ["Options:Initialized:1"] = "3" };

        var original = configuration.GetSection("Options").Get<BinaryOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<BinaryOptions>(configuration, "Options");

        generated.Initialized.ShouldBe(original.Initialized);
        generated.Initialized.ShouldBe([1, 2, 3]);
    }

    [Theory(DisplayName = "Generated binding resolves configuration aliases containing multiple path segments")]
    [InlineData("configured", "42")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void BinderShouldResolveNestedAliases(string? text, string? number)
    {
        var configuration = new ConfigurationManager
        {
            ["Options:NESTED:VALUE"] = text,
            ["Options:Nested:Numbers:Optional"] = number,
            ["Options:Nested:Payload:Name"] = "configured payload",
            ["Options:Nested:Items:0"] = "2",
        };

        var original = configuration.GetSection("Options").Get<AliasedOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<AliasedOptions>(configuration, "Options");

        JsonSerializer.Serialize(generated).ShouldBe(JsonSerializer.Serialize(original));
        generated.Text.ShouldBe(text);
        generated.Payload.Name.ShouldBe("configured payload");
        generated.Items.ShouldBe([1, 2]);
        generated.Missing.ShouldBe(9);
    }

    [Fact(DisplayName = "Generated binding preserves conversion errors for multi-segment aliases")]
    public void BinderShouldPreserveNestedAliasErrors()
    {
        var configuration = new ConfigurationManager { ["Options:Nested:Numbers:Optional"] = "invalid" };

        var original = Should.Throw<InvalidOperationException>(() => configuration.GetSection("Options").Get<AliasedOptions>());
        var generated = Should.Throw<InvalidOperationException>(() =>
            OptionBindingRegistry.GetRequired<AliasedOptions>(configuration, "Options"));

        generated.Message.ShouldContain("Options:Nested:Numbers:Optional");
        generated.InnerException.ShouldBeOfType(original.InnerException!.GetType());
    }

    [Fact(DisplayName = "Generated binding preserves standard and custom TypeConverter values")]
    public void BinderShouldPreserveTypeConverterValues()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Culture"] = "en-US",
            ["Options:Value"] = "converted",
            ["Options:Values:0"] = "first",
            ["Options:Values:1"] = "invalid",
            ["Options:Inherited"] = "inherited",
            ["Options:Number"] = "42",
            ["Options:Numbers:0"] = "13",
        };

        var generated = OptionBindingRegistry.GetRequired<ConverterOptions>(configuration, "Options");
        var original = configuration.GetSection("Options").Get<ConverterOptions>()!;

        generated.Culture.Name.ShouldBe(original.Culture.Name);
        generated.Value.ShouldBe(original.Value);
        generated.Values.ShouldBe(original.Values);
        generated.Inherited!.Value.ShouldBe(original.Inherited!.Value);
        generated.Number.ShouldBe(original.Number);
        generated.Numbers.ShouldBe(original.Numbers);
    }

    [Theory(DisplayName = "Generated binding preserves TypeConverter failures and configuration paths")]
    [InlineData("Culture", "!invalid!")]
    [InlineData("Value", "invalid")]
    public void BinderShouldPreserveTypeConverterErrors(string property, string value)
    {
        var configuration = new ConfigurationManager { [$"Options:{property}"] = value };

        var original = Should.Throw<InvalidOperationException>(() => configuration.GetSection("Options").Get<ConverterOptions>());
        var generated = Should.Throw<InvalidOperationException>(() =>
            OptionBindingRegistry.GetRequired<ConverterOptions>(configuration, "Options"));

        generated.Message.ShouldContain($"Options:{property}");
        generated.InnerException.ShouldBeOfType(original.InnerException!.GetType());
    }

    [Theory(DisplayName = "Generated binding default-initializes structs without invoking parameterized constructors")]
    [InlineData(null)]
    [InlineData("")]
    public void BinderShouldDefaultInitializeStructs(string? value)
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Position:X"] = "3",
            ["Options:Coordinates:X"] = "4",
            ["Options:Positions:0"] = value,
            ["Options:PositionArray:0"] = value,
            ["Options:CoordinatesList:0"] = value,
        };

        var generated = OptionBindingRegistry.GetRequired<StructOptions>(configuration, "Options");
        var original = configuration.GetSection("Options").Get<StructOptions>()!;

        JsonSerializer.Serialize(generated).ShouldBe(JsonSerializer.Serialize(original));
        generated.Positions.Count.ShouldBe(1);
        generated.Positions[0].X.ShouldBe(0);
        generated.CoordinatesList.ShouldBe([default]);
    }

    [Theory(DisplayName = "Generated binding preserves nullable struct defaults for unconvertible scalar leaves")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    public void BinderShouldPreserveNullableStructLeaves(string? value)
    {
        var configuration = new ConfigurationManager { ["Options:OptionalPosition"] = value };

        var generated = OptionBindingRegistry.GetRequired<StructOptions>(configuration, "Options");
        var original = configuration.GetSection("Options").Get<StructOptions>()!;

        generated.OptionalPosition.ShouldBe(original.OptionalPosition);
    }

    [Theory(DisplayName = "Generated binding preserves empty and null values for nullable converted structs")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("18")]
    public void BinderShouldPreserveNullableConvertedValues(string? value)
    {
        var configuration = new ConfigurationManager { ["Options:Number"] = value };

        var generated = OptionBindingRegistry.GetRequired<ConverterOptions>(configuration, "Options");
        var original = configuration.GetSection("Options").Get<ConverterOptions>()!;

        generated.Number.ShouldBe(original.Number);
    }

    [Fact(DisplayName = "Generated binding rejects null converted struct array entries like the original binder")]
    public void BinderShouldRejectNullConvertedArrayEntries()
    {
        var configuration = new ConfigurationManager { ["Options:Numbers:0"] = null };

        Should.Throw<InvalidCastException>(() => configuration.GetSection("Options").Get<ConverterOptions>());
        Should.Throw<InvalidCastException>(() => OptionBindingRegistry.GetRequired<ConverterOptions>(configuration, "Options"));
    }

    [Fact(DisplayName = "Generated binding uses properties when a converter does not accept strings")]
    public void BinderShouldPreserveNonConvertingModels()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Nested:Name"] = "configured",
            ["Options:Values:0:Name"] = "configured item",
            ["Options:Values:1"] = "ignored",
            ["Options:Points:0"] = null,
            ["Options:Points:1"] = "ignored",
            ["Options:Points:2:X"] = "9",
        };

        var generated = OptionBindingRegistry.GetRequired<NonConvertingOptions>(configuration, "Options");
        var original = configuration.GetSection("Options").Get<NonConvertingOptions>()!;

        JsonSerializer.Serialize(generated).ShouldBe(JsonSerializer.Serialize(original));
        generated.Nested.Name.ShouldBe("configured");
    }

    public sealed class IntrinsicOptions : KafkaConsumerOption
    {
        public Version Version { get; init; } = new(1, 0);
        public Half Half { get; init; }
        public Int128 Signed { get; init; }
        public UInt128 Unsigned { get; init; }
        public Half? Optional { get; init; } = (Half)7;
        public List<Version> Versions { get; init; } = [];
    }

    public sealed class DictionaryLookupOptions : KafkaConsumerOption
    {
        public LookupDictionary<int> Numbers { get; } = [];
        public LookupDictionary<int?> Optional { get; } = [];
        public LookupDictionary<ConvertedValue> Converted { get; } = [];
        public LookupDictionary<IConfigurationSection> Sections { get; } = [];
        public LookupDictionary<List<int>> Lists { get; } = new() { ThrowOnLookup = false, ["value"] = [7] };
        public LookupDictionary<byte[]> Bytes { get; } = new() { ThrowOnLookup = false, ["value"] = [1] };
    }

    public sealed class LookupDictionary<T> : Dictionary<string, T>, IDictionary<string, T>
    {
        public bool ThrowOnLookup { get; init; } = true;
        public int Lookups { get; private set; }

        bool IDictionary<string, T>.TryGetValue(string key, out T value)
        {
            Lookups++;
            if (ThrowOnLookup)
            {
                throw new InvalidOperationException("Existing values cannot be read.");
            }

            return TryGetValue(key, out value!);
        }
    }

    public sealed class SingleConversionOptions : KafkaConsumerOption
    {
        public SingleConversionValue? Value { get; init; }
    }

    public sealed class SingleConversionParameter(SingleConversionValue value) : KafkaConsumerOption
    {
        public SingleConversionValue Value { get; } = value;
    }

    public sealed class SingleConversionArray : KafkaConsumerOption
    {
        public SingleConversionValue[] Values { get; init; } = [];
    }

    public sealed class SingleConversionDictionary : KafkaConsumerOption
    {
        public Dictionary<string, SingleConversionValue> Entries { get; init; } = [];
    }

    [TypeConverter(typeof(SingleConversionConverter))]
    public readonly record struct SingleConversionValue(string Name);

    public sealed class SingleConversionConverter : TypeConverter
    {
        public static int Activations { get; set; }

        public SingleConversionConverter()
        {
            if (++Activations > 1)
            {
                throw new InvalidOperationException("Converter constructed more than once.");
            }
        }

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
            => new SingleConversionValue((string)value);
    }

    [TypeConverter(typeof(ScalarOptionsConverter))]
    public sealed class ScalarOptions : KafkaConsumerOption;

    public sealed class ScalarOptionsConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
            => (string)value switch
            {
                "invalid" => throw new NotSupportedException("Unsupported configured options."),
                "null" => null,
                var topic => new ScalarOptions { TopicName = topic },
            };
    }

    public sealed class BinaryOptions : KafkaConsumerOption
    {
        public List<byte[]?> Values { get; init; } = [];
        public byte[]?[] Array { get; init; } = [];
        public Dictionary<string, byte[]?> Dictionary { get; init; } = [];
        public byte[]? Empty { get; init; } = [];
        public byte[]? Initialized { get; init; } = [1];
    }

    public sealed class AliasedOptions : KafkaConsumerOption
    {
        [ConfigurationKeyName("nested:value")]
        public string? Text { get; init; } = "initial";

        [ConfigurationKeyName("Nested:Numbers:Optional")]
        public int? Number { get; init; } = 7;

        [ConfigurationKeyName("Nested:Payload")]
        public NonConvertingValue Payload { get; } = new();

        [ConfigurationKeyName("Nested:Items")]
        public List<int> Items { get; init; } = [1];

        [ConfigurationKeyName("Nested:Missing")]
        public int Missing { get; init; } = 9;
    }

    public sealed class ConverterOptions : KafkaConsumerOption
    {
        public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;
        public ConvertedValue? Value { get; init; }
        public List<ConvertedValue> Values { get; init; } = [];
        public InheritedValue? Inherited { get; init; }
        public ConvertedNumber? Number { get; init; } = new ConvertedNumber(7);
        public ConvertedNumber[] Numbers { get; init; } = [];
    }

    [TypeConverter(typeof(ValueConverter))]
    public sealed record ConvertedValue(string Value);

    public sealed class ValueConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string text && text != "invalid")
            {
                return new ConvertedValue(text);
            }

            throw new NotSupportedException("Unsupported configured value.");
        }
    }

    [TypeConverter(typeof(TypedValueConverter))]
    public class TypedValue
    {
        public string Value { get; init; } = "initial";
    }

    public sealed class InheritedValue : TypedValue;

    public sealed class TypedValueConverter(Type type) : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            type.ShouldBe(typeof(InheritedValue));
            return new InheritedValue { Value = (string)value };
        }
    }

    [TypeConverter(typeof(NumberConverter))]
    public readonly record struct ConvertedNumber(int Value);

    public sealed class NumberConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
            => new ConvertedNumber(int.Parse((string)value, CultureInfo.InvariantCulture));
    }

    public sealed class NonConvertingOptions : KafkaConsumerOption
    {
        public NonConvertingValue Nested { get; } = new();
        public List<NonConvertingValue> Values { get; init; } = [];
        public NonConvertingPoint[] Points { get; init; } = [];
    }

    [TypeConverter(typeof(TypeConverter))]
    public sealed class NonConvertingValue
    {
        public string Name { get; init; } = "initial";
    }

    [TypeConverter(typeof(TypeConverter))]
    public readonly struct NonConvertingPoint
    {
        public int X { get; init; }
    }

    public sealed class StructOptions : KafkaConsumerOption
    {
        public Position Position { get; init; }
        public Coordinates Coordinates { get; init; }
        public Position? OptionalPosition { get; init; } = new Position(5);
        public List<Position> Positions { get; init; } = [];
        public Position[] PositionArray { get; init; } = [];
        public List<Coordinates> CoordinatesList { get; init; } = [];
    }

    public readonly struct Position(int x)
    {
        public int X { get; init; } = x + 100;
        public int Y { get; init; } = 200;
    }

    public readonly record struct Coordinates(int X, int Y);
}
