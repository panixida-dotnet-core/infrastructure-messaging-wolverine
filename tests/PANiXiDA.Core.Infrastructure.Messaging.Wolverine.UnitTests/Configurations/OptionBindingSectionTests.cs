using System.ComponentModel;
using System.Globalization;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class OptionBindingSectionTests
{
    [Fact(DisplayName = "Generated read-only dictionary binding copies existing entries through non-generic enumeration")]
    public void BinderShouldEnumerateCopiedDictionary()
    {
        var configuration = new ConfigurationManager { ["Options:Values:added"] = "2" };

        var original = configuration.GetSection("Options").Get<DictionaryCopyOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<DictionaryCopyOptions>(configuration, "Options");

        generated.Values.ShouldBe(original.Values);
        generated.Values["existing"].ShouldBe(1);
        generated.Values["added"].ShouldBe(2);
    }

    [Fact(DisplayName = "Generated array-compatible binding enumerates existing values instead of using CopyTo")]
    public void BinderShouldEnumerateCopiedSequence()
    {
        var configuration = new ConfigurationManager { ["Options:Values:0"] = "2" };

        var original = configuration.GetSection("Options").Get<SequenceCopyOptions<EnumerationOnlyList>>()!;
        var generated = OptionBindingRegistry.GetRequired<SequenceCopyOptions<EnumerationOnlyList>>(configuration, "Options");

        generated.Values.ShouldBe(original.Values);
        generated.Values.ShouldBe([1, 2]);
    }

    [Fact(DisplayName = "Generated array-compatible binding preserves failures when enumerating existing values")]
    public void BinderShouldPreserveCopiedSequenceEnumerationFailure()
    {
        var configuration = new ConfigurationManager { ["Options:Values:0"] = "2" };

        var original = Should.Throw<FormatException>(() => configuration.GetSection("Options").Get<SequenceCopyOptions<FailingEnumerationList>>());
        var generated = Should.Throw<FormatException>(() => OptionBindingRegistry.GetRequired<SequenceCopyOptions<FailingEnumerationList>>(configuration, "Options"));

        generated.Message.ShouldBe(original.Message);
    }

    [Fact(DisplayName = "Generated binding dispatches Add through the declared set interface")]
    public void BinderShouldUseSetInterfaceAdd()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Values:0"] = "configured",
            ["Options:Concrete:0"] = "configured",
        };

        var original = configuration.GetSection("Options").Get<SetDispatchOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<SetDispatchOptions>(configuration, "Options");

        original.Values.ShouldBe(["configured"]);
        generated.Values.ShouldBe(original.Values.AsEnumerable());
        generated.Concrete.ShouldBe(original.Concrete);
        generated.Concrete.ShouldBeEmpty();
    }

    [Theory(DisplayName = "Generated property binding preserves declared key casing in conversion errors")]
    [InlineData("count", "Count")]
    [InlineData("nestedname:othercount", "NestedName:OtherCount")]
    public void BinderShouldPreserveDeclaredKeyCasing(string providerKey, string declaredKey)
    {
        var configuration = new ConfigurationManager { [$"Options:{providerKey}"] = "invalid" };

        var original = Should.Throw<InvalidOperationException>(() => configuration.GetSection("Options").Get<CaseSensitivePathOptions>());
        var generated = Should.Throw<InvalidOperationException>(() => OptionBindingRegistry.GetRequired<CaseSensitivePathOptions>(configuration, "Options"));

        original.Message.ShouldContain($"Options:{declaredKey}");
        generated.Message.ShouldBe(original.Message);
    }

    [Fact(DisplayName = "Generated binding preserves declared parent casing in nested section paths")]
    public void BinderShouldPreserveDeclaredParentCasing()
    {
        var configuration = new ConfigurationManager { ["Options:payload:section:key"] = "configured" };

        var original = configuration.GetSection("Options").Get<CaseSensitivePathOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<CaseSensitivePathOptions>(configuration, "Options");

        generated.Payload.Section.Path.ShouldBe(original.Payload.Section.Path);
        generated.Payload.Section.Path.ShouldBe("Options:Payload:Section");
    }

    [Fact(DisplayName = "Generated collections skip entries that produce no value")]
    public void BinderShouldOmitUnboundCollectionEntries()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Sets:0:0"] = "1",
            ["Options:Maps:0:true"] = "1",
        };

        var original = configuration.GetSection("Options").Get<NestedCollectionOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<NestedCollectionOptions>(configuration, "Options");

        generated.Sets.ShouldBe(original.Sets);
        generated.Maps.ShouldBe(original.Maps);
        generated.Sets.ShouldBeEmpty();
        generated.Maps.ShouldBeEmpty();
    }

    [Theory(DisplayName = "Generated array-like constructor arguments preserve scalar sequences")]
    [InlineData("array")]
    [InlineData("enumerable")]
    [InlineData("collection")]
    [InlineData("list")]
    public void BinderShouldPreserveConstructorArraySeeds(string kind)
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Value"] = "ab",
            ["Options:Value:0"] = "c",
        };

        switch (kind)
        {
            case "array": AssertSeed<char[]>(configuration); break;
            case "enumerable": AssertSeed<IEnumerable<char>>(configuration); break;
            case "collection": AssertSeed<IReadOnlyCollection<char>>(configuration); break;
            default: AssertSeed<IReadOnlyList<char>>(configuration); break;
        }
    }

    [Fact(DisplayName = "Generated array constructor arguments preserve incompatible scalar seed failures")]
    public void BinderShouldRejectIncompatibleArraySeeds()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Value"] = "ab",
            ["Options:Value:0"] = "configured",
        };

        var original = Record.Exception(() => configuration.GetSection("Options").Get<OptionBindingFailureTests.ParameterOptions<string[]>>());
        var generated = Record.Exception(() => OptionBindingRegistry.GetRequired<OptionBindingFailureTests.ParameterOptions<string[]>>(configuration, "Options"));

        original.ShouldNotBeNull();
        generated.ShouldNotBeNull();
        generated.GetType().ShouldBe(original.GetType());
        generated.Message.ShouldBe(original.Message);
    }

    private static void AssertSeed<T>(ConfigurationManager configuration) where T : IEnumerable<char>
    {
        var original = configuration.GetSection("Options").Get<OptionBindingFailureTests.ParameterOptions<T>>()!;
        var generated = OptionBindingRegistry.GetRequired<OptionBindingFailureTests.ParameterOptions<T>>(configuration, "Options");

        generated.Value.ToArray().ShouldBe(original.Value.ToArray());
        generated.Value.ToArray().ShouldBe(['a', 'b', 'c']);
    }

    [Fact(DisplayName = "Generated scalar constructor parameters preserve explicit null results with children")]
    public void BinderShouldPreserveNullScalarParameters()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Value"] = null,
            ["Options:Value:Ignored"] = "1",
        };

        var original = Should.Throw<InvalidOperationException>(() =>
            configuration.GetSection("Options").Get<OptionBindingFailureTests.ParameterOptions<int>>());
        var generated = Should.Throw<InvalidOperationException>(() =>
            OptionBindingRegistry.GetRequired<OptionBindingFailureTests.ParameterOptions<int>>(configuration, "Options"));
        var originalOptional = configuration.GetSection("Options").Get<OptionalScalarOptions>()!;
        var generatedOptional = OptionBindingRegistry.GetRequired<OptionalScalarOptions>(configuration, "Options");

        generated.Message.ShouldBe(original.Message);
        generatedOptional.Value.ShouldBe(originalOptional.Value);
        generatedOptional.Value.ShouldBe(42);
    }

    [Fact(DisplayName = "Generated binding calls struct constructors only for absent values")]
    public void BinderShouldInitializeNewStructs()
    {
        var configuration = new ConfigurationManager();
        foreach (var path in new[] { "Values:0", "Array:0", "Entries:new", "Entries:existing", "Optional", "Existing", "Throwing:0" })
        {
            configuration[$"Options:{path}:Ignored"] = "1";
        }

        var original = configuration.GetSection("Options").Get<StructOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<StructOptions>(configuration, "Options");

        generated.Values.Single().Value.ShouldBe(original.Values.Single().Value);
        generated.Array.Single().Value.ShouldBe(original.Array.Single().Value);
        generated.Entries["new"].Value.ShouldBe(original.Entries["new"].Value);
        generated.Entries["existing"].Value.ShouldBe(original.Entries["existing"].Value);
        generated.Optional.ShouldBe(original.Optional);
        generated.Existing.Value.ShouldBe(original.Existing.Value);
        generated.Throwing.ShouldBe(original.Throwing);
        generated.Throwing.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Generated binding preserves high-precision decimal constructor defaults")]
    public void BinderShouldPreserveDecimalDefaults()
    {
        var configuration = new ConfigurationManager { ["Options:TopicName"] = "configured" };

        var original = configuration.GetSection("Options").Get<DecimalOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<DecimalOptions>(configuration, "Options");

        generated.Rate.ShouldBe(original.Rate);
        generated.Rate.ShouldBe(0.1234567890123456789012345678m);
    }

    [Fact(DisplayName = "Generated binding preserves lazy getter mutations in nested structs")]
    public void BinderShouldPreserveStructGetterMutations()
    {
        var configuration = new ConfigurationManager { ["Options:Value:Child:Name"] = "configured" };

        var original = configuration.GetSection("Options").Get<OptionBindingEdgeTests.ValueOptions<LazyModel>>()!;
        var generated = OptionBindingRegistry.GetRequired<OptionBindingEdgeTests.ValueOptions<LazyModel>>(configuration, "Options");

        generated.Value.Child.Name.ShouldBe(original.Value.Child.Name);
        generated.Value.Child.Name.ShouldBe("configured");
    }

    [Fact(DisplayName = "Generated binding validates child properties of scalar models")]
    public void BinderShouldValidateScalarChildren()
    {
        var configuration = new ConfigurationManager { ["Options:Value:Major"] = "invalid" };

        var original = Should.Throw<InvalidOperationException>(() =>
            configuration.GetSection("Options").Get<ReferenceScalarOptions>());
        var generated = Should.Throw<InvalidOperationException>(() =>
            OptionBindingRegistry.GetRequired<ReferenceScalarOptions>(configuration, "Options"));

        generated.Message.ShouldBe(original.Message);
    }

    [Fact(DisplayName = "Generated binding populates culture format properties from child keys")]
    public void BinderShouldUpdateCultureChildren()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Culture:NumberFormat:NumberDecimalSeparator"] = "~",
            ["Options:Culture:DateTimeFormat:DateSeparator"] = "*",
        };

        var original = configuration.GetSection("Options").Get<ReferenceScalarOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<ReferenceScalarOptions>(configuration, "Options");

        generated.Culture.NumberFormat.NumberDecimalSeparator.ShouldBe(original.Culture.NumberFormat.NumberDecimalSeparator);
        generated.Culture.NumberFormat.NumberDecimalSeparator.ShouldBe("~");
        generated.Culture.DateTimeFormat.DateSeparator.ShouldBe(original.Culture.DateTimeFormat.DateSeparator);
        generated.Culture.DateTimeFormat.DateSeparator.ShouldBe("*");
    }

    [Fact(DisplayName = "Generated binding reports configured polymorphic framework properties")]
    public void BinderShouldRejectAbstractFrameworkMembers()
    {
        var configuration = new ConfigurationManager { ["Options:Culture:Calendar:TwoDigitYearMax"] = "2050" };

        var error = Should.Throw<NotSupportedException>(() =>
            OptionBindingRegistry.GetRequired<ReferenceScalarOptions>(configuration, "Options"));

        error.Message.ShouldContain("Options:Culture:Calendar");
    }

    [Theory(DisplayName = "Generated binding ignores read-only complex getters for unconvertible scalar leaves")]
    [InlineData(false)]
    [InlineData(true)]
    public void BinderShouldIgnoreUnconvertibleReadOnlyLeaves(bool customConverter)
    {
        var configuration = new ConfigurationManager { ["Options:Value"] = "invalid" };

        if (customConverter) AssertBinds<OptionBindingEdgeTests.ThrowingGetterOptions<OptionBindingFailureTests.NonConvertingPayload>>(configuration);
        else AssertBinds<OptionBindingEdgeTests.ThrowingGetterOptions<OptionBindingFailureTests.Payload>>(configuration);
    }

    [Theory(DisplayName = "Generated binding rejects unconvertible constructor scalars with children")]
    [InlineData("")]
    [InlineData("invalid")]
    public void BinderShouldRejectConstructorScalarsWithChildren(string scalar)
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Value"] = scalar,
            ["Options:Value:Name"] = "configured",
        };

        var original = Should.Throw<ArgumentException>(() => configuration.GetSection("Options").Get<OptionBindingFailureTests.ParameterOptions<OptionBindingFailureTests.Payload>>());
        var generated = Should.Throw<ArgumentException>(() => OptionBindingRegistry.GetRequired<OptionBindingFailureTests.ParameterOptions<OptionBindingFailureTests.Payload>>(configuration, "Options"));

        generated.Message.ShouldBe(original.Message);
    }

    [Fact(DisplayName = "Generated binding converts explicit nulls before binding children")]
    public void BinderShouldPreferExplicitNullToChildren()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Value"] = null,
            ["Options:Value:Name"] = "configured",
            ["Options:Values:0"] = null,
            ["Options:Values:0:Name"] = "configured",
            ["Options:Entries:key"] = null,
            ["Options:Entries:key:Name"] = "configured",
        };

        var original = configuration.GetSection("Options").Get<ConvertedOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<ConvertedOptions>(configuration, "Options");

        original.Value.ShouldBeNull();
        generated.Value.ShouldBe(original.Value);
        generated.Values.ShouldBe(original.Values);
        generated.Values.ShouldBe([null]);
        generated.Entries.ShouldBe(original.Entries);
        generated.Entries["key"].ShouldBeNull();
    }

    [Fact(DisplayName = "Generated binding enumerates an existing read-only set with configured children")]
    public void BinderShouldEnumerateReadOnlySets()
    {
        var configuration = new ConfigurationManager { ["Options:Values:0"] = "configured" };

        var original = configuration.GetSection("Options").Get<ReadOnlySetOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<ReadOnlySetOptions>(configuration, "Options");

        var originalSet = original.Values.ShouldBeOfType<TrackingSet>();
        var generatedSet = generated.Values.ShouldBeOfType<TrackingSet>();
        originalSet.Enumerations.ShouldBe(1);
        generatedSet.Enumerations.ShouldBe(originalSet.Enumerations);
        generatedSet.Contains("configured").ShouldBeFalse();
    }

    [Theory(DisplayName = "Generated binding discards unassignable converter results for read-only properties")]
    [InlineData(false)]
    [InlineData(true)]
    public void BinderShouldDiscardReadOnlyConverterResults(bool nullable)
    {
        var configuration = new ConfigurationManager { ["Options:Value"] = "configured" };

        if (nullable) AssertBinds<OptionBindingEdgeTests.ThrowingGetterOptions<ConvertedValue?>>(configuration);
        else AssertBinds<OptionBindingEdgeTests.ThrowingGetterOptions<ConvertedPayload>>(configuration);
    }

    private static void AssertBinds<T>(ConfigurationManager configuration) where T : class
    {
        configuration.GetSection("Options").Get<T>().ShouldNotBeNull();
        OptionBindingRegistry.GetRequired<T>(configuration, "Options").ShouldNotBeNull();
    }

    public sealed class DecimalOptions(decimal rate = 0.1234567890123456789012345678m) : KafkaConsumerOption
    {
        public decimal Rate { get; } = rate;
    }

    public sealed class CaseSensitivePathOptions : KafkaConsumerOption
    {
        public int Count { get; init; }
        [ConfigurationKeyName("NestedName:OtherCount")]
        public int AliasedCount { get; init; }
        public PathPayload Payload { get; init; } = new();
    }

    public sealed class PathPayload
    {
        public IConfigurationSection Section { get; init; } = null!;
    }

    public sealed class OptionalScalarOptions(int value = 42) : KafkaConsumerOption
    {
        public int Value { get; } = value;
    }

    public sealed class NestedCollectionOptions : KafkaConsumerOption
    {
        public List<ISet<int>> Sets { get; init; } = [];
        public IReadOnlyDictionary<bool, int>[] Maps { get; init; } = [];
    }

    public sealed class StructOptions : KafkaConsumerOption
    {
        public List<OptionBindingLeafTests.DefaultStruct> Values { get; init; } = [];
        public OptionBindingLeafTests.DefaultStruct[] Array { get; init; } = [];
        public Dictionary<string, OptionBindingLeafTests.DefaultStruct> Entries { get; init; } = new() { ["existing"] = default };
        public OptionBindingLeafTests.DefaultStruct? Optional { get; init; }
        public OptionBindingLeafTests.DefaultStruct Existing { get; init; }
        public List<ThrowingStruct> Throwing { get; init; } = [];
    }

    public struct ThrowingStruct
    {
        public ThrowingStruct() => throw new InvalidOperationException("Constructor invoked.");
    }

    public struct LazyModel
    {
        private OptionBindingFailureTests.Payload? child;
        public OptionBindingFailureTests.Payload Child => child ??= new();
    }

    public sealed class ReferenceScalarOptions : KafkaConsumerOption
    {
        public Version Value { get; init; } = new(7, 0);
        public CultureInfo Culture { get; init; } = new("en-US");
    }

    public sealed class ConvertedOptions : KafkaConsumerOption
    {
        public ConvertedPayload? Value { get; init; } = new();
        public List<ConvertedPayload?> Values { get; init; } = [];
        public Dictionary<string, ConvertedPayload?> Entries { get; init; } = [];
    }

    [TypeConverter(typeof(IncompatibleConverter))]
    public sealed class ConvertedPayload
    {
        public string Name { get; init; } = "initial";
    }

    [TypeConverter(typeof(IncompatibleConverter))]
    public readonly record struct ConvertedValue(int Value);

    public sealed class IncompatibleConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);
        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => value;
    }

    public sealed class ReadOnlySetOptions : KafkaConsumerOption
    {
        public IReadOnlySet<string> Values { get; } = new TrackingSet();
    }

    public sealed class SetDispatchOptions : KafkaConsumerOption
    {
        public ISet<string> Values { get; } = new DistinctSet();
        public DistinctSet Concrete { get; } = [];
    }

    public sealed class SequenceCopyOptions<T> : KafkaConsumerOption where T : ICollection<int>, new()
    {
        public IEnumerable<int> Values { get; init; } = new T();
    }

    public sealed class DictionaryCopyOptions : KafkaBrokerOption
    {
        public IReadOnlyDictionary<string, int> Values { get; init; } = new EnumerationOnlyDictionary();
    }

    public sealed class EnumerationOnlyDictionary : Dictionary<string, int>, ICollection<KeyValuePair<string, int>>
    {
        public EnumerationOnlyDictionary() => Add("existing", 1);

        int ICollection<KeyValuePair<string, int>>.Count => throw new InvalidOperationException("Use enumeration instead of reading Count.");
        IEnumerator<KeyValuePair<string, int>> IEnumerable<KeyValuePair<string, int>>.GetEnumerator()
            => throw new InvalidOperationException("Use the non-generic enumerator.");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class EnumerationOnlyList : List<int>, ICollection<int>
    {
        private static readonly int[] InitialValues = [1];

        public EnumerationOnlyList() => Add(1);

        void ICollection<int>.CopyTo(int[] array, int arrayIndex) => throw new InvalidOperationException("CopyTo must not run.");
        IEnumerator<int> IEnumerable<int>.GetEnumerator() => throw new InvalidOperationException("Use the non-generic enumerator.");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => InitialValues.GetEnumerator();
    }

    public sealed class FailingEnumerationList : List<int>, ICollection<int>
    {
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new FormatException("Enumeration failed.");
    }

    public sealed class DistinctSet : HashSet<string>, ISet<string>
    {
        bool ISet<string>.Add(string item) => Add(item);
        void ICollection<string>.Add(string item) => throw new InvalidOperationException("Wrong Add method.");
    }

    public sealed class TrackingSet : HashSet<string>, IReadOnlySet<string>
    {
        public int Enumerations { get; private set; }

        IEnumerator<string> IEnumerable<string>.GetEnumerator()
        {
            Enumerations++;
            return GetEnumerator();
        }
    }
}
