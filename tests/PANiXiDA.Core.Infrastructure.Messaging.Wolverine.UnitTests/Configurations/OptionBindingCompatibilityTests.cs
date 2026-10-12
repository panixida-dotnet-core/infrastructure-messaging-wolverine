using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class OptionBindingCompatibilityTests
{
    [Fact(DisplayName = "Generated dictionaries mutate existing reference values without invoking their indexer setters")]
    public void BinderShouldPreserveDictionaryReferenceValues()
    {
        var configuration = new ConfigurationManager();
        foreach (var entry in new[] { "existing", "created", "empty" })
        {
            configuration[$"Options:Objects:{entry}:Name"] = "configured";
            configuration[$"Options:Lists:{entry}:0"] = "8";
            configuration[$"Options:Maps:{entry}:number"] = "8";
            configuration[$"Options:Collections:{entry}:0"] = "8";
        }

        var original = configuration.GetSection("Options").Get<ReferenceDictionaryOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<ReferenceDictionaryOptions>(configuration, "Options");

        generated.Objects["existing"].ShouldBeSameAs(generated.InitialObject);
        generated.Objects["existing"].Name.ShouldBe("configured");
        generated.Objects["created"].Name.ShouldBe(original.Objects["created"].Name);
        generated.Objects["empty"].Name.ShouldBe(original.Objects["empty"].Name);
        generated.Lists["existing"].ShouldBeSameAs(generated.InitialList);
        generated.Lists["existing"].ShouldBe([7, 8]);
        generated.Maps["existing"].ShouldBeSameAs(generated.InitialMap);
        generated.Maps["existing"].ShouldBe(original.Maps["existing"]);
        generated.Collections["existing"].ShouldBeSameAs(generated.InitialCollection);
        generated.Collections["existing"].ShouldBe(original.Collections["existing"]);
        generated.Objects.Writes.ShouldBe(original.Objects.Writes);
        generated.Lists.Writes.ShouldBe(original.Lists.Writes);
        generated.Maps.Writes.ShouldBe(original.Maps.Writes);
        generated.Collections.Writes.ShouldBe(original.Collections.Writes);
        generated.Objects.Writes.ShouldBe(2);
        generated.Objects.Lookups.ShouldBe(original.Objects.Lookups);
        generated.Lists.Lookups.ShouldBe(original.Lists.Lookups);
    }

    [Fact(DisplayName = "Generated dictionaries retain indexer writes for replaced collections and converted values")]
    public void BinderShouldWriteReplacedDictionaryValues()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Maps:existing:number"] = "8",
            ["Options:Sets:existing:0"] = "configured",
            ["Options:Arrays:existing:0"] = "invalid",
            ["Options:Converted:existing"] = "configured",
        };

        var original = configuration.GetSection("Options").Get<ReplacingDictionaryOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<ReplacingDictionaryOptions>(configuration, "Options");

        generated.Maps["existing"].ShouldBe(original.Maps["existing"]);
        generated.Sets["existing"].ShouldBe(original.Sets["existing"]);
        generated.Arrays["existing"].ShouldBe(original.Arrays["existing"]);
        generated.Converted["existing"].ShouldBe(original.Converted["existing"]);
        generated.Maps.Writes.ShouldBe(original.Maps.Writes);
        generated.Sets.Writes.ShouldBe(original.Sets.Writes);
        generated.Arrays.Writes.ShouldBe(original.Arrays.Writes);
        generated.Converted.Writes.ShouldBe(original.Converted.Writes);
        generated.Maps.Writes.ShouldBe(1);
        generated.Sets.Writes.ShouldBe(1);
        generated.Arrays.Writes.ShouldBe(1);
        generated.Converted.Writes.ShouldBe(1);
        generated.Converted.Lookups.ShouldBe(0);
    }

    [Fact(DisplayName = "Generated dictionaries preserve existing references for empty sections")]
    public void BinderShouldRetainDictionaryReferencesForEmptySections()
    {
        var configuration = new ConfigurationManager();
        foreach (var property in new[] { "Objects", "Lists", "Maps", "Collections", "Sets", "Arrays" })
        {
            configuration[$"Options:{property}:existing"] = "";
        }

        var original = configuration.GetSection("Options").Get<ReferenceDictionaryOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<ReferenceDictionaryOptions>(configuration, "Options");
        var originalCollections = configuration.GetSection("Options").Get<ReplacingDictionaryOptions>()!;
        var generatedCollections = OptionBindingRegistry.GetRequired<ReplacingDictionaryOptions>(configuration, "Options");

        generated.Objects.Writes.ShouldBe(original.Objects.Writes);
        generated.Lists.Writes.ShouldBe(original.Lists.Writes);
        generated.Maps.Writes.ShouldBe(original.Maps.Writes);
        generated.Collections.Writes.ShouldBe(original.Collections.Writes);
        generatedCollections.Maps.Writes.ShouldBe(originalCollections.Maps.Writes);
        generatedCollections.Sets.Writes.ShouldBe(originalCollections.Sets.Writes);
        generatedCollections.Arrays.Writes.ShouldBe(originalCollections.Arrays.Writes);
        generated.Objects.Writes.ShouldBe(0);
        generated.Objects["existing"].ShouldBeSameAs(generated.InitialObject);
        generated.Objects["existing"].Name.ShouldBe("initial");
    }

    [Theory(DisplayName = "Generated dictionaries replace byte arrays only when binding produces a new value")]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("AgM=", false)]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(" ", true)]
    [InlineData("AgM=", true)]
    public void BinderShouldTrackBinaryDictionaryReplacements(string? value, bool hasChildren)
    {
        var configuration = new ConfigurationManager();
        foreach (var entry in new[] { "existing", "empty", "created" })
        {
            configuration[$"Options:Bytes:{entry}"] = value;
            if (hasChildren)
            {
                configuration[$"Options:Bytes:{entry}:0"] = "4";
            }
        }

        var original = configuration.GetSection("Options").Get<BinaryDictionaryOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<BinaryDictionaryOptions>(configuration, "Options");

        foreach (var entry in original.Bytes)
        {
            generated.Bytes[entry.Key].ShouldBe(entry.Value);
        }
        generated.Bytes.Writes.ShouldBe(original.Bytes.Writes);
        generated.Bytes.Lookups.ShouldBe(original.Bytes.Lookups);
        ReferenceEquals(generated.Bytes["existing"], generated.InitialBytes)
            .ShouldBe(ReferenceEquals(original.Bytes["existing"], original.InitialBytes));
    }

    [Theory(DisplayName = "Generated binding skips nullable converters for null and empty values")]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(null, true)]
    [InlineData("", true)]
    public void BinderShouldSkipNullableConverters(string? value, bool hasChildren)
    {
        using var json = new MemoryStream(System.Text.Encoding.UTF8.GetBytes($$"""
            { "Options": {
                "Value": {{JsonSerializer.Serialize(value)}},
                "Values": [{{JsonSerializer.Serialize(value)}}],
                "Array": [{{JsonSerializer.Serialize(value)}}],
                "Entries": { "entry": {{JsonSerializer.Serialize(value)}} }
            } }
            """));
        var configuration = new ConfigurationManager();
        configuration.AddJsonStream(json);
        if (hasChildren)
        {
            foreach (var path in new[] { "Value", "Values:0", "Array:0", "Entries:entry" })
            {
                configuration[$"Options:{path}:Number"] = "42";
            }
        }

        var original = configuration.GetSection("Options").Get<NullableConverterOptions>()!;
        var originalParameter = configuration.GetSection("Options").Get<NullableConverterParameter>()!;
        var generated = OptionBindingRegistry.GetRequired<NullableConverterOptions>(configuration, "Options");
        var generatedParameter = OptionBindingRegistry.GetRequired<NullableConverterParameter>(configuration, "Options");

        original.Value.ShouldBeNull();
        generated.Value.ShouldBe(original.Value);
        generated.Values.ShouldBe(original.Values);
        generated.Values.ShouldBe([null]);
        generated.Array.ShouldBe(original.Array);
        generated.Array.ShouldBe([null]);
        generated.Entries.ShouldBe(original.Entries);
        generated.Entries["entry"].ShouldBeNull();
        generatedParameter.Value.ShouldBe(originalParameter.Value);
        generatedParameter.Value.ShouldBeNull();
    }

    [Fact(DisplayName = "Generated nullable binding retains child-only sections without resolving converters")]
    public void BinderShouldBindNullableConverterChildren()
    {
        var configuration = new ConfigurationManager();
        foreach (var path in new[] { "Value", "Values:0", "Array:0", "Entries:entry" })
        {
            configuration[$"Options:{path}:Number"] = "42";
        }

        var original = configuration.GetSection("Options").Get<NullableConverterOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<NullableConverterOptions>(configuration, "Options");

        generated.Value.ShouldBe(original.Value);
        generated.Value!.Value.Number.ShouldBe(42);
        generated.Values.ShouldBe(original.Values);
        generated.Values.Single()!.Value.Number.ShouldBe(42);
        generated.Array.ShouldBe(original.Array);
        generated.Array.Single()!.Value.Number.ShouldBe(42);
        generated.Entries.ShouldBe(original.Entries);
        generated.Entries["entry"]!.Value.Number.ShouldBe(42);
    }

    [Fact(DisplayName = "Generated dictionaries write back existing complex structs and populate new entries")]
    public void BinderShouldWriteBackDictionaryStructs()
    {
        var configuration = new ConfigurationManager();
        foreach (var property in new[] { "Positions", "Optional", "ReadOnly" })
        {
            configuration[$"Options:{property}:existing:X"] = "42";
            configuration[$"Options:{property}:created:X"] = "43";
        }

        var original = configuration.GetSection("Options").Get<DictionaryStructOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<DictionaryStructOptions>(configuration, "Options");

        original.Positions["existing"].X.ShouldBe(42);
        original.Positions["existing"].Y.ShouldBe(200);
        original.Positions["created"].X.ShouldBe(43);
        generated.Positions.ShouldBe(original.Positions);
        generated.Optional.ShouldBe(original.Optional);
        generated.ReadOnly.ShouldBe(original.ReadOnly);
        generated.Positions.Lookups.ShouldBe(original.Positions.Lookups);
        generated.Positions.Writes.ShouldBe(original.Positions.Writes);
        generated.Positions.Writes.ShouldBe(2);
    }

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

    public sealed class ReferenceDictionaryOptions : KafkaConsumerOption
    {
        internal ReferenceValue InitialObject { get; } = new();
        internal List<int> InitialList { get; } = [7];
        internal Dictionary<string, int> InitialMap { get; } = new() { ["initial"] = 7 };
        internal ICollection<int> InitialCollection { get; } = [7];
        public LookupDictionary<ReferenceValue> Objects { get; }
        public LookupDictionary<List<int>> Lists { get; }
        public LookupDictionary<Dictionary<string, int>> Maps { get; }
        public LookupDictionary<ICollection<int>> Collections { get; }

        public ReferenceDictionaryOptions()
        {
            Objects = new() { ThrowOnLookup = false, ThrowOnExistingWrite = true, ["existing"] = InitialObject, ["empty"] = null! };
            Lists = new() { ThrowOnLookup = false, ThrowOnExistingWrite = true, ["existing"] = InitialList, ["empty"] = null! };
            Maps = new() { ThrowOnLookup = false, ThrowOnExistingWrite = true, ["existing"] = InitialMap, ["empty"] = null! };
            Collections = new() { ThrowOnLookup = false, ThrowOnExistingWrite = true, ["existing"] = InitialCollection, ["empty"] = null! };
        }
    }

    public sealed class ReferenceValue
    {
        public string Name { get; init; } = "initial";
    }

    public sealed class ReplacingDictionaryOptions : KafkaConsumerOption
    {
        public LookupDictionary<IDictionary<string, int>> Maps { get; } = new() { ThrowOnLookup = false, ["existing"] = new Dictionary<string, int>() };
        public LookupDictionary<ISet<string>> Sets { get; } = new() { ThrowOnLookup = false, ["existing"] = new HashSet<string>() };
        public LookupDictionary<int[]> Arrays { get; } = new() { ThrowOnLookup = false, ["existing"] = [] };
        public LookupDictionary<ConvertedValue> Converted { get; } = new() { ["existing"] = new ConvertedValue("initial") };
    }

    public sealed class BinaryDictionaryOptions : KafkaConsumerOption
    {
        internal byte[] InitialBytes { get; } = [1];
        public LookupDictionary<byte[]?> Bytes { get; }

        public BinaryDictionaryOptions()
        {
            Bytes = new() { ThrowOnLookup = false, ["existing"] = InitialBytes, ["empty"] = [] };
        }
    }

    public sealed class NullableConverterOptions : KafkaConsumerOption
    {
        public NullableConvertedValue? Value { get; init; } = new NullableConvertedValue { Number = 7 };
        public List<NullableConvertedValue?> Values { get; init; } = [];
        public NullableConvertedValue?[] Array { get; init; } = [];
        public Dictionary<string, NullableConvertedValue?> Entries { get; init; } = [];
    }

    public sealed class NullableConverterParameter(NullableConvertedValue? value = null) : KafkaConsumerOption
    {
        public NullableConvertedValue? Value { get; } = value;
    }

    [TypeConverter(typeof(ThrowingNullableConverter))]
    public readonly struct NullableConvertedValue
    {
        public int Number { get; init; }
    }

    public sealed class ThrowingNullableConverter : TypeConverter
    {
        public ThrowingNullableConverter() => throw new InvalidOperationException("Nullable converter must not be constructed.");
    }

    public sealed class DictionaryStructOptions : KafkaConsumerOption
    {
        public LookupDictionary<Position> Positions { get; } = new() { ThrowOnLookup = false, ["existing"] = new Position(5) };
        public Dictionary<string, Position?> Optional { get; init; } = new() { ["existing"] = new Position(5) };
        public IReadOnlyDictionary<string, Position> ReadOnly { get; init; } = new Dictionary<string, Position> { ["existing"] = new Position(5) };
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
        public bool ThrowOnExistingWrite { get; init; }
        public int Lookups { get; private set; }
        public int Writes { get; private set; }

        T IDictionary<string, T>.this[string key]
        {
            get => this[key];
            set
            {
                Writes++;
                if (ThrowOnExistingWrite && TryGetValue(key, out var previous) && previous is not null)
                {
                    throw new InvalidOperationException("Existing entries cannot be replaced.");
                }
                this[key] = value;
            }
        }

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
