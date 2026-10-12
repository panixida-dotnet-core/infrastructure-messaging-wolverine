using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class OptionBindingMemberTests
{
    [Fact(DisplayName = "Generated binding preserves hidden base properties and their configuration aliases")]
    public void BinderShouldBindHiddenBaseProperties()
    {
        var configuration = new ConfigurationManager { ["Options:TopicName"] = "base-topic", ["Options:Alias"] = "derived-topic" };

        var original = configuration.GetSection("Options").Get<HiddenOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<HiddenOptions>(configuration, "Options");

        generated.TopicName.ShouldBe(original.TopicName);
        ((KafkaConsumerOption)generated).TopicName.ShouldBe(((KafkaConsumerOption)original).TopicName);
        ((KafkaConsumerOption)generated).TopicName.ShouldBe("base-topic");
    }

    [Fact(DisplayName = "Generated binding addresses hidden properties through their declaring types")]
    public void BinderShouldBindDifferentHiddenPropertyTypes()
    {
        var configuration = new ConfigurationManager { ["Options:TopicName"] = "42" };

        var original = configuration.GetSection("Options").Get<HiddenNumberOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<HiddenNumberOptions>(configuration, "Options");

        generated.TopicName.ShouldBe(original.TopicName);
        ((KafkaConsumerOption)generated).TopicName.ShouldBe(((KafkaConsumerOption)original).TopicName);
    }

    [Fact(DisplayName = "Generated binding uses base metadata and invokes virtual setters once")]
    public void BinderShouldPreserveVirtualProperties()
    {
        var configuration = new ConfigurationManager { ["Options:BaseName"] = "base-name", ["Options:DerivedName"] = "derived-name" };

        var original = configuration.GetSection("Options").Get<OverrideOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<OverrideOptions>(configuration, "Options");

        generated.Name.ShouldBe(original.Name);
        generated.SetterCalls.ShouldBe(original.SetterCalls);
        generated.SetterCalls.ShouldBe(1);
    }

    [Fact(DisplayName = "Generated binding preserves virtual init setter dispatch")]
    public void BinderShouldPreserveVirtualInitProperties()
    {
        var configuration = new ConfigurationManager { ["Options:BaseName"] = "base-name", ["Options:DerivedName"] = "derived-name" };

        var original = configuration.GetSection("Options").Get<OverrideInitOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<OverrideInitOptions>(configuration, "Options");

        generated.Name.ShouldBe(original.Name);
        generated.SetterCalls.ShouldBe(original.SetterCalls);
    }

    [Fact(DisplayName = "Generated binding preserves repeated binding of getter-only virtual collections")]
    public void BinderShouldPreserveGetterOnlyVirtualCollections()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Values:0"] = "1",
            ["Options:Values:1"] = "2",
        };

        var original = configuration.GetSection("Options").Get<OverrideCollectionOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<OverrideCollectionOptions>(configuration, "Options");

        original.Values.ShouldBe([1, 2, 1, 2]);
        generated.Values.ShouldBe(original.Values);
    }

    [Fact(DisplayName = "Generated binding populates properties of concrete enumerable-only models")]
    public void BinderShouldBindEnumerableObjects()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Payload:Name"] = "payload",
            ["Options:Pages:Name"] = "pages",
        };

        var original = configuration.GetSection("Options").Get<EnumerableOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<EnumerableOptions>(configuration, "Options");

        generated.Payload.Name.ShouldBe(original.Payload.Name);
        generated.Payload.Name.ShouldBe("payload");
        generated.Pages.Name.ShouldBe(original.Pages.Name);
        generated.Pages.Name.ShouldBe("pages");
    }

    [Fact(DisplayName = "Generated binding populates ordinary properties of concrete read-only dictionary models")]
    public void BinderShouldBindReadOnlyDictionaryObjects()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Payload:Name"] = "configured",
            ["Options:Sealed:Name"] = "sealed",
            ["Options:Created:Name"] = "created",
        };

        var original = configuration.GetSection("Options").Get<ReadOnlyDictionaryOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<ReadOnlyDictionaryOptions>(configuration, "Options");

        generated.Payload.Name.ShouldBe(original.Payload.Name);
        generated.Payload.Name.ShouldBe("configured");
        generated.Payload["existing"].ShouldBe(original.Payload["existing"]);
        generated.Sealed.Name.ShouldBe(original.Sealed.Name);
        generated.Sealed.Name.ShouldBe("sealed");
        generated.Created.ShouldNotBeNull();
        generated.Created.Name.ShouldBe(original.Created!.Name);
        generated.Created.Name.ShouldBe("created");
    }

    [Theory(DisplayName = "Generated binding preserves constructor-bound property accessor side effects")]
    [InlineData("configured")]
    [InlineData(null)]
    public void BinderShouldResetConstructorBoundProperties(string? name)
    {
        var configuration = new ConfigurationManager { ["Options:TopicName"] = "topic" };
        if (name is not null)
        {
            configuration["Options:Name"] = name;
        }

        var original = configuration.GetSection("Options").Get<ConstructorOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<ConstructorOptions>(configuration, "Options");

        generated.Name.ShouldBe(original.Name);
        generated.SetterCalls.ShouldBe(original.SetterCalls);
        generated.SetterCalls.ShouldBe(1);
    }

    [Fact(DisplayName = "Generated binding skips non-writable constructor-bound getters after creation")]
    public void BinderShouldSkipNonWritableConstructorBoundGetters()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Value:Name"] = "configured",
            ["Options:PrivateValue:Name"] = "private",
            ["Options:Values:0"] = "42",
        };

        var original = configuration.GetSection("Options").Get<ReadOnlyConstructorOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<ReadOnlyConstructorOptions>(configuration, "Options");

        generated.GetterCalls.ShouldBe(original.GetterCalls);
        generated.GetterCalls.ShouldBe(0);
        generated.CapturedValue.Name.ShouldBe(original.CapturedValue.Name);
        generated.CapturedValue.Name.ShouldBe("configured");
        generated.CapturedPrivateValue.Name.ShouldBe(original.CapturedPrivateValue.Name);
        generated.CapturedPrivateValue.Name.ShouldBe("private");
        generated.CapturedValues.ShouldBe(original.CapturedValues);
        generated.CapturedValues.ShouldBe([42]);
    }

    [Fact(DisplayName = "Generated binding still populates non-writable properties of existing constructor models")]
    public void BinderShouldBindExistingNonWritableConstructorProperties()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Existing:Value:Name"] = "configured",
            ["Options:Existing:PrivateValue:Name"] = "private",
            ["Options:Existing:Values:0"] = "42",
        };

        var original = configuration.GetSection("Options").Get<ExistingConstructorOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<ExistingConstructorOptions>(configuration, "Options");

        generated.Existing.GetterCalls.ShouldBe(original.Existing.GetterCalls);
        generated.Existing.GetterCalls.ShouldBe(3);
        generated.Existing.CapturedValue.Name.ShouldBe(original.Existing.CapturedValue.Name);
        generated.Existing.CapturedValue.Name.ShouldBe("configured");
        generated.Existing.CapturedPrivateValue.Name.ShouldBe(original.Existing.CapturedPrivateValue.Name);
        generated.Existing.CapturedPrivateValue.Name.ShouldBe("private");
        generated.Existing.CapturedValues.ShouldBe(original.Existing.CapturedValues);
        generated.Existing.CapturedValues.ShouldBe([1, 42]);
    }

    [Theory(DisplayName = "Generated binding assigns absent and present configuration section properties")]
    [InlineData(false)]
    [InlineData(true)]
    public void BinderShouldBindSectionProperties(bool configured)
    {
        var configuration = new ConfigurationManager { ["Options:TopicName"] = "topic" };
        if (configured)
        {
            configuration["Options:Missing:Value"] = "value";
            configuration["Options:Nested:Aliased:Value"] = "aliased";
        }

        var original = configuration.GetSection("Options").Get<SectionOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<SectionOptions>(configuration, "Options");

        generated.Missing.ShouldNotBeNull();
        generated.Missing.Path.ShouldBe(original.Missing!.Path);
        generated.Missing["Value"].ShouldBe(original.Missing["Value"]);
        generated.Aliased.ShouldNotBeNull();
        generated.Aliased.Path.ShouldBe(original.Aliased!.Path);
        generated.Initialized.Path.ShouldBe(original.Initialized.Path);
        generated.ReadOnly.Path.ShouldBe(original.ReadOnly.Path);
    }

    [Theory(DisplayName = "Generated binding preserves getter exception chains and conversion ordering")]
    [InlineData("missing")]
    [InlineData("nested")]
    [InlineData("binary")]
    [InlineData("invalid-number")]
    public void BinderShouldPreserveGetterFailures(string scenario)
    {
        var configuration = new ConfigurationManager { ["Options:TopicName"] = "topic" };
        switch (scenario)
        {
            case "nested":
                configuration["Options:Value:Name"] = "configured";
                AssertSameFailure<ThrowingGetterOptions<Payload>>(configuration);
                break;
            case "binary":
                configuration["Options:Value"] = "AQI=";
                AssertSameFailure<ThrowingGetterOptions<byte[]>>(configuration);
                break;
            case "invalid-number":
                configuration["Options:Value"] = "invalid";
                AssertSameFailure<ThrowingGetterOptions<int>>(configuration);
                break;
            default:
                AssertSameFailure<ThrowingGetterOptions<string>>(configuration);
                break;
        }
    }

    [Theory(DisplayName = "Generated binding does not read getters when replacing configured scalars")]
    [InlineData("configured")]
    [InlineData("")]
    [InlineData(null)]
    public void BinderShouldSkipReplacedScalarGetters(string? value)
    {
        var configuration = new ConfigurationManager { ["Options:Value"] = value };

        var original = configuration.GetSection("Options").Get<ThrowingGetterOptions<string>>()!;
        var generated = OptionBindingRegistry.GetRequired<ThrowingGetterOptions<string>>(configuration, "Options");

        generated.TopicName.ShouldBe(original.TopicName);
    }

    private static void AssertSameFailure<T>(ConfigurationManager configuration) where T : class
    {
        var original = Record.Exception(() => configuration.GetSection("Options").Get<T>());
        var generated = Record.Exception(() => OptionBindingRegistry.GetRequired<T>(configuration, "Options"));

        original.ShouldNotBeNull("The original binder must reject this input.");
        generated.ShouldNotBeNull("The generated binder must reject this input.");
        generated.GetType().ShouldBe(original.GetType());
        generated.InnerException.ShouldNotBeNull();
        generated.InnerException.GetType().ShouldBe(original.InnerException!.GetType());
    }

    public sealed class HiddenOptions : KafkaConsumerOption
    {
        [ConfigurationKeyName("Alias")]
        public new string TopicName { get; init; } = "initial";
    }

    public sealed class HiddenNumberOptions : KafkaConsumerOption
    {
        public new int TopicName { get; init; }
    }

    public class VirtualOptions : KafkaConsumerOption
    {
        [ConfigurationKeyName("BaseName")]
        public virtual string Name { get; set; } = "initial";
    }

    public sealed class OverrideOptions : VirtualOptions
    {
        public int SetterCalls { get; private set; }

        [ConfigurationKeyName("DerivedName")]
        public override string Name
        {
            get => base.Name;
            set { SetterCalls++; base.Name = value; }
        }
    }

    public class VirtualInitOptions : KafkaConsumerOption
    {
        [ConfigurationKeyName("BaseName")]
        public virtual string Name { get; init; } = "initial";
    }

    public sealed class OverrideInitOptions : VirtualInitOptions
    {
        public int SetterCalls { get; private set; }

        [ConfigurationKeyName("DerivedName")]
        public override string Name
        {
            get => base.Name;
            init { SetterCalls++; base.Name = value; }
        }
    }

    public sealed class EnumerableOptions : KafkaConsumerOption
    {
        public EnumerablePayload Payload { get; init; } = new();
        public ReadOnlyEnumerablePayload Pages { get; } = new();
    }

    public class VirtualCollectionOptions : KafkaConsumerOption
    {
        public virtual List<int> Values { get; } = [];
    }

    public sealed class OverrideCollectionOptions : VirtualCollectionOptions
    {
        public override List<int> Values => base.Values;
    }

    public class EnumerablePayload : IEnumerable<int>
    {
        public string Name { get; init; } = "initial";
        public IEnumerator<int> GetEnumerator() => Enumerable.Empty<int>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class ReadOnlyDictionaryOptions : KafkaConsumerOption
    {
        public ReadOnlyDictionaryPayload Payload { get; } = new();
        public SealedDictionaryPayload Sealed { get; } = new();
        public ReadOnlyDictionaryPayload? Created { get; init; }
    }

    public class ReadOnlyDictionaryPayload : IReadOnlyDictionary<string, string>
    {
        private readonly Dictionary<string, string> values = new() { ["existing"] = "initial" };
        public string Name { get; init; } = "initial";
        public string this[string key] => values[key];
        public IEnumerable<string> Keys => values.Keys;
        public IEnumerable<string> Values => values.Values;
        public int Count => values.Count;
        public bool ContainsKey(string key) => values.ContainsKey(key);
        public bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class ConstructorOptions(string? Name = null) : KafkaConsumerOption
    {
        private string? name = Name;
        public string? Name
        {
            get => name;
            init { name = value; SetterCalls++; }
        }
        public int SetterCalls { get; private set; }
    }

    public sealed class ReadOnlyConstructorOptions(Payload Value, Payload PrivateValue, List<int> Values) : KafkaConsumerOption
    {
        public readonly Payload CapturedValue = Value;
        public readonly Payload CapturedPrivateValue = PrivateValue;
        public readonly List<int> CapturedValues = Values;
        public bool ThrowOnRead = true;
        public int GetterCalls { get; private set; }
        public Payload Value => Read(CapturedValue);
        public List<int> Values => Read(CapturedValues);
        public Payload PrivateValue
        {
            get => Read(CapturedPrivateValue);
            private set => throw new InvalidOperationException("Private setter must not be called.");
        }

        private T Read<T>(T value)
        {
            GetterCalls++;
            return ThrowOnRead ? throw new FormatException("Constructor-bound getter must not be called.") : value;
        }
    }

    public sealed class ExistingConstructorOptions : KafkaConsumerOption
    {
        public ReadOnlyConstructorOptions Existing { get; } = new(new Payload(), new Payload(), [1]) { ThrowOnRead = false };
    }

    public sealed class SealedDictionaryPayload : ReadOnlyDictionaryPayload;

    public sealed class ReadOnlyEnumerablePayload : EnumerablePayload, IReadOnlyCollection<int>
    {
        public int Count => 0;
    }

    public sealed class SectionOptions : KafkaConsumerOption
    {
        public IConfigurationSection? Missing { get; init; }
        [ConfigurationKeyName("Nested:Aliased")]
        public IConfigurationSection? Aliased { get; init; }
        public IConfigurationSection Initialized { get; init; } = new ConfigurationManager().GetSection("initial");
        public IConfigurationSection ReadOnly { get; } = new ConfigurationManager().GetSection("read-only");
    }

    public sealed class ThrowingGetterOptions<T> : KafkaConsumerOption
    {
        public T Value
        {
            get => throw new FormatException("Getter failed.");
            set => TopicName = value?.ToString() ?? "null";
        }
    }

    public sealed class Payload
    {
        public string Name { get; init; } = "initial";
    }
}
