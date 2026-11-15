using System.ComponentModel;
using System.Globalization;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class OptionBindingSectionTests
{
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
