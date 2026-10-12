using System.ComponentModel;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class PolymorphicOptionBindingTests
{
    [Theory(DisplayName = "Generated binding populates the runtime properties of initialized base, interface and object members")]
    [InlineData("Base")]
    [InlineData("Interface")]
    [InlineData("Generic")]
    [InlineData("Object")]
    [InlineData("ReadOnlyObject")]
    [InlineData("FactoryObject")]
    [InlineData("ObjectMap:existing")]
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

    [Theory(DisplayName = "Generated object binding preserves scalar precedence and existing instances")]
    [InlineData(false, null)]
    [InlineData(false, "")]
    [InlineData(false, "scalar")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "scalar")]
    public void BinderShouldPreserveObjectValues(bool readOnly, string? value)
    {
        var member = readOnly ? "ReadOnlyObject" : "Object";
        var configuration = new ConfigurationManager { [$"Options:{member}"] = value, [$"Options:{member}:alias"] = "configured" };

        var original = configuration.GetSection("Options").Get<PolymorphicOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<PolymorphicOptions>(configuration, "Options");
        var expected = readOnly ? original.ReadOnlyObject : original.Object;
        var actual = readOnly ? generated.ReadOnlyObject : generated.Object;

        if (expected is null)
        {
            actual.ShouldBeNull();
        }
        else if (expected is string text)
        {
            actual.ShouldBe(text);
        }
        else
        {
            ((ConcreteSettings)actual).Name.ShouldBe(((ConcreteSettings)expected).Name);
            actual.ShouldBeSameAs(readOnly ? generated.InitialReadOnlyObject : generated.InitialObject);
        }
    }

    [Fact(DisplayName = "Generated binding discovers initialized object types declared by a base option model")]
    public void BinderShouldBindInheritedObjects()
    {
        var configuration = new ConfigurationManager { ["Options:Value:Name"] = "configured" };

        var original = configuration.GetSection("Options").Get<InheritedObjectOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<InheritedObjectOptions>(configuration, "Options");

        ((InheritedPayload)generated.Value).Name.ShouldBe(((InheritedPayload)original.Value).Name);
        ((InheritedPayload)generated.Value).Name.ShouldBe("configured");
    }

    [Theory(DisplayName = "Generated binding reports inaccessible runtime implementations instead of ignoring their properties")]
    [InlineData("Value")]
    [InlineData("Map:existing")]
    [InlineData("Object")]
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
        "Object" => (ConcreteSettings)options.Object,
        "ReadOnlyObject" => (ConcreteSettings)options.ReadOnlyObject,
        "FactoryObject" => (ConcreteSettings)options.FactoryObject,
        "ObjectMap:existing" => (ConcreteSettings)options.ObjectMap["existing"],
        _ => (ConcreteSettings)options.Map["existing"],
    };

    public sealed class PolymorphicOptions : KafkaConsumerOption
    {
        public BaseSettings Base { get; init; } = new LeafSettings();
        public ISettings Interface { get; } = new ConcreteSettings();
        public ISettings Generic { get; } = new GenericSettings<int>();
        public object Object { get; init; }
        public object ReadOnlyObject => InitialReadOnlyObject;
        public object FactoryObject { get; } = CreateSettings();
        public Dictionary<string, object> ObjectMap { get; } = new() { ["existing"] = new LeafSettings() };
        public Dictionary<string, BaseSettings> Map { get; } = new() { ["existing"] = new ConcreteSettings() };
        internal LeafSettings InitialObject { get; } = new();
        internal GenericSettings<int> InitialReadOnlyObject { get; } = new();
        public PolymorphicOptions() => Object = InitialObject;
        private static ConcreteSettings CreateSettings() => new();
    }

    public interface ISettings;

    public class InheritedObjectBaseOptions : KafkaConsumerOption
    {
        public object Value { get; } = new InheritedPayload();
    }

    public sealed class InheritedObjectOptions : InheritedObjectBaseOptions;

    public sealed class InheritedPayload
    {
        public string Name { get; init; } = "initial";
    }

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
        public new string GetType() => Name;
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
        public object Object { get; } = new HiddenSettings();
        public Dictionary<string, ISettings> Map { get; } = new() { ["existing"] = new HiddenSettings() };
        private sealed class HiddenSettings : ISettings
        {
            public string Name { get; init; } = "initial";
        }
    }
}
