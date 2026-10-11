using System.ComponentModel;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class PolymorphicOptionBindingTests
{
    [Theory(DisplayName = "Generated binding populates the runtime properties of initialized base and interface members")]
    [InlineData("Base")]
    [InlineData("Interface")]
    [InlineData("Generic")]
    [InlineData("Map:existing")]
    public void BinderShouldBindRuntimeProperties(string member)
    {
        var configuration = new ConfigurationManager
        {
            [$"Options:{member}:alias"] = "configured",
            [$"Options:{member}:Extra:Name"] = "nested",
            [$"Options:{member}:Values:0"] = "42",
            [$"Options:{member}:Leaf"] = "leaf",
            [$"Options:{member}:Item"] = "7",
        };

        var original = configuration.GetSection("Options").Get<PolymorphicOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<PolymorphicOptions>(configuration, "Options");
        var expected = GetSettings(original, member);
        var actual = GetSettings(generated, member);

        actual.Name.ShouldBe(expected.Name);
        actual.Name.ShouldBe("configured");
        actual.Extra.Name.ShouldBe(expected.Extra.Name);
        actual.Extra.Name.ShouldBe("nested");
        actual.Values.ShouldBe(expected.Values);
        actual.Values.ShouldBe([42]);
        if (actual is LeafSettings leaf)
        {
            leaf.Leaf.ShouldBe(((LeafSettings)expected).Leaf);
            leaf.Leaf.ShouldBe("leaf");
        }
        if (actual is GenericSettings<int> generic)
        {
            generic.Item.ShouldBe(((GenericSettings<int>)expected).Item);
            generic.Item.ShouldBe(7);
        }
    }

    [Fact(DisplayName = "Generated runtime dispatch preserves the instance and does not reapply runtime converters or constructors")]
    public void BinderShouldPreserveExistingInstances()
    {
        var configuration = new ConfigurationManager { ["Options:Value"] = "scalar", ["Options:Value:Name"] = "configured" };

        var original = configuration.GetSection("Options").Get<ConverterOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<ConverterOptions>(configuration, "Options");

        generated.Value.Name.ShouldBe(original.Value.Name);
        generated.Value.Name.ShouldBe("configured");
        ((ConvertedSettings)generated.Value).Name.ShouldBe(((ConvertedSettings)original.Value).Name);
        ((ConvertedSettings)generated.Value).Name.ShouldBe("configured");
        generated.Value.ShouldBeSameAs(generated.Initial);
    }

    [Theory(DisplayName = "Generated binding reports inaccessible runtime implementations instead of ignoring their properties")]
    [InlineData("Value")]
    [InlineData("Map:existing")]
    public void BinderShouldRejectUnknownRuntimeTypes(string member)
    {
        var configuration = new ConfigurationManager { [$"Options:{member}:Name"] = "configured" };

        var error = Should.Throw<NotSupportedException>(() => OptionBindingRegistry.GetRequired<HiddenOptions>(configuration, "Options"));

        error.Message.ShouldContain("Options:" + member);
        error.Message.ShouldContain("HiddenSettings");
    }

    private static ConcreteSettings GetSettings(PolymorphicOptions options, string member) => member switch
    {
        "Base" => (ConcreteSettings)options.Base,
        "Interface" => (ConcreteSettings)options.Interface,
        "Generic" => (ConcreteSettings)options.Generic,
        _ => (ConcreteSettings)options.Map["existing"],
    };

    public sealed class PolymorphicOptions : KafkaConsumerOption
    {
        public BaseSettings Base { get; init; } = new LeafSettings();
        public ISettings Interface { get; } = new ConcreteSettings();
        public ISettings Generic { get; } = new GenericSettings<int>();
        public Dictionary<string, BaseSettings> Map { get; } = new() { ["existing"] = new ConcreteSettings() };
    }

    public interface ISettings;

    public abstract class BaseSettings;

    public class ConcreteSettings : BaseSettings, ISettings
    {
        [ConfigurationKeyName("alias")]
        public string Name { get; init; } = "initial";
        public Payload Extra { get; init; } = new();
        public List<int> Values { get; } = [];
    }

    public sealed class LeafSettings : ConcreteSettings
    {
        public string Leaf { get; init; } = "initial";
    }

    public sealed class GenericSettings<T> : ConcreteSettings
    {
        public T? Item { get; init; }
    }

    public sealed class Payload
    {
        public string Name { get; init; } = "initial";
    }

    public sealed class ConverterOptions : KafkaConsumerOption
    {
        public PlainSettings Value { get; }
        internal PlainSettings Initial { get; } = new ConvertedSettings("initial");
        public ConverterOptions() => Value = Initial;
    }

    public class PlainSettings
    {
        public string Name { get; init; } = "initial";
        public new string GetType() => "hidden";
    }

    [TypeConverter(typeof(ThrowingConverter))]
    public sealed class ConvertedSettings(string name) : PlainSettings
    {
        public new string Name { get; init; } = name.ToUpperInvariant();
    }

    public sealed class ThrowingConverter : TypeConverter
    {
        public ThrowingConverter() => throw new InvalidOperationException("Runtime converter must not be used.");
    }

    public sealed class HiddenOptions : KafkaConsumerOption
    {
        public ISettings Value { get; } = new HiddenSettings();
        public Dictionary<string, ISettings> Map { get; } = new() { ["existing"] = new HiddenSettings() };
        private sealed class HiddenSettings : ISettings
        {
            public string Name { get; init; } = "initial";
        }
    }
}
