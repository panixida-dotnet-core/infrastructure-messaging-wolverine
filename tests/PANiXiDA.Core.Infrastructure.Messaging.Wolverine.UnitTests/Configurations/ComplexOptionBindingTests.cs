using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class ComplexOptionBindingTests
{
    [Fact(DisplayName = "Generated binding matches reflection for nested objects, collections and init setters")]
    public void BinderShouldPreserveComplexOptions()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:TopicName"] = "events",
            ["Options:Label"] = "configured",
            ["Options:Nested:retry-count"] = "5",
            ["Options:Nested:Next:Name"] = "child",
            ["Options:ReadOnly:Name"] = "updated",
            ["Options:Numbers:0"] = "2",
            ["Options:Numbers:1"] = "invalid",
            ["Options:Numbers:2"] = "3",
            ["Options:Items:0:Name"] = "item",
            ["Options:ReadOnlyItems:0:Name"] = "read-only item",
            ["Options:Map:existing:Name"] = "changed",
            ["Options:Map:new:retry-count"] = "7",
            ["Options:ReadOnlyMap:new:0"] = "10",
            ["Options:Tags:0"] = "old",
            ["Options:Tags:1"] = "new",
            ["Options:Sequence:0"] = "next",
            ["Options:Immutable:Name"] = "record",
            ["Options:Immutable:Attempts"] = "8",
            ["Options:Position:X"] = "4",
            ["Options:OptionalPosition:X"] = "6",
        };

        var generated = OptionBindingRegistry.GetRequired<ComplexOptions>(configuration, "Options");
        var original = configuration.GetSection("Options").Get<ComplexOptions>()!;

        JsonSerializer.Serialize(generated).ShouldBe(JsonSerializer.Serialize(original));
        generated.InitCalls.ShouldBe(1);
        generated.Nested.Next!.Name.ShouldBe("child");
        generated.ReadOnlyItems.Count.ShouldBe(2);
    }

    [Fact(DisplayName = "Generated binding preserves absent init defaults and constructor state")]
    public void BinderShouldPreserveAbsentDefaults()
    {
        var configuration = new ConfigurationManager { ["Options:TopicName"] = "events" };

        var generated = OptionBindingRegistry.GetRequired<ComplexOptions>(configuration, "Options");
        var original = configuration.GetSection("Options").Get<ComplexOptions>()!;

        JsonSerializer.Serialize(generated).ShouldBe(JsonSerializer.Serialize(original));
        generated.Label.ShouldBe("default");
    }

    [Theory(DisplayName = "Generated binding preserves explicit nulls and invalid nested conversions")]
    [InlineData(null)]
    [InlineData("invalid")]
    public void BinderShouldMatchNestedFailures(string? value)
    {
        var configuration = new ConfigurationManager { ["Options:Nested:retry-count"] = value };

        if (value is null)
        {
            var generated = OptionBindingRegistry.GetRequired<ComplexOptions>(configuration, "Options");
            var original = configuration.GetSection("Options").Get<ComplexOptions>()!;
            JsonSerializer.Serialize(generated).ShouldBe(JsonSerializer.Serialize(original));
        }
        else
        {
            var generated = Should.Throw<InvalidOperationException>(() => OptionBindingRegistry.GetRequired<ComplexOptions>(configuration, "Options"));
            var original = Should.Throw<InvalidOperationException>(() => configuration.GetSection("Options").Get<ComplexOptions>());
            generated.Message.ShouldBe(original.Message);
        }
    }

    [Theory(DisplayName = "Generated binding preserves null and empty collection values")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ignored")]
    public void BinderShouldPreserveCollectionLeaves(string? value)
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Nested"] = value,
            ["Options:Numbers"] = value,
            ["Options:Items"] = value,
            ["Options:Map"] = value,
            ["Options:Sequence"] = value,
            ["Options:ReadOnly"] = value,
            ["Options:ReadOnlyMap"] = value,
            ["Options:Immutable"] = value,
            ["Options:Tags"] = value,
        };

        var generated = OptionBindingRegistry.GetRequired<ComplexOptions>(configuration, "Options");
        var original = configuration.GetSection("Options").Get<ComplexOptions>()!;

        JsonSerializer.Serialize(generated).ShouldBe(JsonSerializer.Serialize(original));
    }

    [Fact(DisplayName = "Generated binding supports required and inherited generic init setters")]
    public void BinderShouldSupportRequiredAndGenericInitProperties()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Value:Name"] = "generic",
            ["Options:Required:Name"] = "required",
            ["Options:Required:Attempts"] = "4",
            ["Options:Generic:Value:Name"] = "required generic",
        };

        var generated = OptionBindingRegistry.GetRequired<GenericOptions>(configuration, "Options");
        var original = configuration.GetSection("Options").Get<GenericOptions>()!;

        JsonSerializer.Serialize(generated).ShouldBe(JsonSerializer.Serialize(original));
    }

    [Fact(DisplayName = "Generated binding preserves mutable, immutable and getter-only collection contracts")]
    public void BinderShouldPreserveCollectionInterfaces()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:List:0"] = "one",
            ["Options:Collection:0"] = "2",
            ["Options:ReadOnlyList:0"] = "three",
            ["Options:ReadOnlyCollection:0"] = "four",
            ["Options:Set:0"] = "five",
            ["Options:ReadOnlySet:0"] = "six",
            ["Options:Dictionary:1:0"] = "8",
            ["Options:EnumDictionary:Friday"] = "9",
            ["Options:GetterArray:0"] = "ignored",
            ["Options:GetterSequence:0"] = "ignored",
            ["Options:NullList:0"] = "ignored",
            ["Options:NullNested:Name"] = "ignored",
            ["Options:CustomList:0:Name"] = "custom",
            ["Options:CustomDictionary:new:Name"] = "custom",
            ["Options:Bytes"] = "AgM=",
        };

        var generated = OptionBindingRegistry.GetRequired<CollectionOptions>(configuration, "Options");
        var original = configuration.GetSection("Options").Get<CollectionOptions>()!;

        JsonSerializer.Serialize(generated).ShouldBe(JsonSerializer.Serialize(original));
    }

    [Fact(DisplayName = "Generated dictionary binding preserves the current culture for integer keys")]
    public void BinderShouldPreserveDictionaryKeyCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NegativeSign = "~";
        CultureInfo.CurrentCulture = culture;
        try
        {
            var configuration = new ConfigurationManager { ["Options:Dictionary:~1:0"] = "5" };

            var generated = OptionBindingRegistry.GetRequired<CollectionOptions>(configuration, "Options");
            var original = configuration.GetSection("Options").Get<CollectionOptions>()!;

            generated.Dictionary[-1].ShouldBe(original.Dictionary[-1]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact(DisplayName = "Generated binding preserves parameterized constructor transformations and binds collection arguments once")]
    public void BinderShouldPreserveParameterizedConstructors()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Record:Name"] = "transformed",
            ["Options:Record:Values:0"] = "1",
        };

        var generated = OptionBindingRegistry.GetRequired<ConstructorOptions>(configuration, "Options");
        var original = configuration.GetSection("Options").Get<ConstructorOptions>()!;

        JsonSerializer.Serialize(generated).ShouldBe(JsonSerializer.Serialize(original));
        generated.Record!.Name.ShouldBe("TRANSFORMED");
        generated.Record.Values.ShouldBe([1]);
    }

    [Fact(DisplayName = "Generated binding populates existing objects that cannot be constructed by the binder")]
    public void BinderShouldPopulateExistingInstances()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:PrivateConstructor:Name"] = "private constructor",
            ["Options:MultipleConstructors:Name"] = "multiple constructors",
        };

        var generated = OptionBindingRegistry.GetRequired<ExistingInstanceOptions>(configuration, "Options");
        var original = configuration.GetSection("Options").Get<ExistingInstanceOptions>()!;

        JsonSerializer.Serialize(generated).ShouldBe(JsonSerializer.Serialize(original));
    }

    [Theory(DisplayName = "Generated collection binding preserves empty objects and skips unconvertible object entries")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    public void BinderShouldPreserveEmptyCollectionEntries(string? value)
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Items:0"] = value,
            ["Options:Map:new"] = value,
        };

        var generated = OptionBindingRegistry.GetRequired<ComplexOptions>(configuration, "Options");
        var original = configuration.GetSection("Options").Get<ComplexOptions>()!;

        JsonSerializer.Serialize(generated).ShouldBe(JsonSerializer.Serialize(original));
    }

    [Fact(DisplayName = "Generated binding rejects null entries in non-nullable arrays like the reflection binder")]
    public void BinderShouldRejectNullValueTypeArrayEntries()
    {
        var configuration = new ConfigurationManager { ["Options:Numbers:0"] = null };

        Should.Throw<InvalidCastException>(() => configuration.GetSection("Options").Get<ComplexOptions>());
        Should.Throw<InvalidCastException>(() => OptionBindingRegistry.GetRequired<ComplexOptions>(configuration, "Options"));
    }

    public sealed class ExistingInstanceOptions : KafkaConsumerOption
    {
        public Settings PrivateConstructor { get; init; } = Settings.Create();
        public MultipleConstructors MultipleConstructors { get; init; } = new("initial");
    }

    public sealed class Settings
    {
        private Settings() { }
        public string Name { get; set; } = "initial";
        public static Settings Create() => new();
    }

    public sealed class MultipleConstructors
    {
        public MultipleConstructors(string name) { Name = name; }
        public MultipleConstructors(int number) { Name = number.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        public string Name { get; set; }
    }

    public sealed class ConstructorOptions : KafkaConsumerOption
    {
        public TransformedSettings? Record { get; init; }
    }

    public sealed class TransformedSettings(string Name, List<int> Values)
    {
        public string Name { get; init; } = Name.ToUpperInvariant();
        public List<int> Values { get; init; } = Values;
    }

    public sealed class GenericOptions : GenericBase<NestedSettings>
    {
        public required RequiredSettings Required { get; init; }
        public RequiredGeneric<NestedSettings>? Generic { get; init; }
    }

    public sealed class RequiredGeneric<T> where T : class, new()
    {
        public required T Value { get; init; }
    }

    public class GenericBase<T> : KafkaConsumerOption
    {
        public T? Value { get; init; }
    }

    public sealed class RequiredSettings
    {
        public required string Name { get; init; }
        public int Attempts { get; init; } = 2;
    }

    public sealed class CollectionOptions : KafkaConsumerOption
    {
        public IList<string>? List { get; init; }
        public ICollection<int>? Collection { get; init; }
        public IReadOnlyList<string>? ReadOnlyList { get; init; }
        public IReadOnlyCollection<string>? ReadOnlyCollection { get; init; }
        public HashSet<string> Set { get; } = ["existing"];
        public IReadOnlySet<string>? ReadOnlySet { get; init; }
        public IDictionary<int, List<int>> Dictionary { get; } = new Dictionary<int, List<int>> { [1] = [7] };
        public Dictionary<DayOfWeek, int> EnumDictionary { get; init; } = [];
        public string[] GetterArray { get; } = ["existing"];
        public IEnumerable<string> GetterSequence { get; } = ["existing"];
        public IList<string>? NullList { get; }
        public NestedSettings? NullNested { get; }
        public CustomList CustomList { get; init; } = [];
        public CustomDictionary CustomDictionary { get; init; } = [];
        public byte[] Bytes { get; init; } = [1];
    }

    public sealed class CustomList : List<NestedSettings>;
    public sealed class CustomDictionary : Dictionary<string, NestedSettings>;

    public sealed class ComplexOptions : KafkaConsumerOption
    {
        private string label = "default";

        public string Label
        {
            get => label;
            init { label = value; InitCalls++; }
        }

        public int InitCalls { get; private set; }
        public NestedSettings Nested { get; init; } = new();
        public NestedSettings ReadOnly { get; } = new();
        public int[] Numbers { get; init; } = [1];
        public List<NestedSettings> Items { get; init; } = [];
        public List<NestedSettings> ReadOnlyItems { get; } = [new()];
        public Dictionary<string, NestedSettings> Map { get; init; } = new() { ["existing"] = new() };
        public IReadOnlyDictionary<string, int[]> ReadOnlyMap { get; init; } = new Dictionary<string, int[]> { ["old"] = [1] };
        public ISet<string> Tags { get; init; } = new HashSet<string> { "old" };
        public IEnumerable<string> Sequence { get; init; } = ["first"];
        public ImmutableSettings? Immutable { get; init; }
        public Position Position { get; init; }
        public Position? OptionalPosition { get; init; }
    }

    public sealed class NestedSettings
    {
        public string Name { get; init; } = "initial";
        [ConfigurationKeyName("retry-count")]
        public int Attempts { get; set; } = 3;
        public NestedSettings? Next { get; init; }
    }

    public sealed record ImmutableSettings(string Name, int Attempts = 2, DayOfWeek Day = DayOfWeek.Friday);

    public struct Position
    {
        public required int X { get; init; }
        public int Y { get; set; }
    }
}
