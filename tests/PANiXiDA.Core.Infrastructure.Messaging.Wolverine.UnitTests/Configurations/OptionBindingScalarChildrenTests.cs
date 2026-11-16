using System.Globalization;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

using static PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations.OptionBindingCompatibilityTests;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class OptionBindingScalarChildrenTests
{
    [Fact(DisplayName = "Generated binding preserves initialized scalars when only child keys exist")]
    public void BinderShouldPreserveScalarDefaults()
    {
        var configuration = new ConfigurationManager();
        foreach (var name in new[] { "Count", "Text", "Optional", "EmptyOptional", "Day", "Id", "Delay", "Price", "Version", "EmptyVersion", "Address" })
            configuration[$"Options:{name}:Ignored"] = "1";

        var original = configuration.GetSection("Options").Get<ScalarOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<ScalarOptions>(configuration, "Options");

        generated.Count.ShouldBe(original.Count);
        generated.Count.ShouldBe(7);
        generated.Text.ShouldBe(original.Text);
        generated.Optional.ShouldBe(original.Optional);
        generated.EmptyOptional.ShouldBe(original.EmptyOptional);
        generated.EmptyOptional.ShouldBe(0);
        generated.Day.ShouldBe(original.Day);
        generated.Id.ShouldBe(original.Id);
        generated.Delay.ShouldBe(original.Delay);
        generated.Price.ShouldBe(original.Price);
        generated.Version.ShouldBe(original.Version);
        generated.EmptyVersion.ShouldBe(original.EmptyVersion);
        generated.EmptyVersion.ShouldBe(new Version());
        generated.Address.ShouldBe(original.Address);
    }

    [Theory(DisplayName = "Generated scalar binding preserves getter and init setter calls for child and value sections")]
    [InlineData("children")]
    [InlineData("null-children")]
    [InlineData("value-children")]
    [InlineData("missing")]
    public void BinderShouldPreserveScalarAccessors(string shape)
    {
        var configuration = new ConfigurationManager { ["Options:TopicName"] = "topic" };
        foreach (var name in new[] { "Count", "ReadOnlyCount" })
        {
            if (shape != "missing") configuration[$"Options:{name}:Ignored"] = "1";
            if (shape is "null-children" or "value-children") configuration[$"Options:{name}"] = shape == "null-children" ? null : "9";
        }

        var original = configuration.GetSection("Options").Get<AccessorOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<AccessorOptions>(configuration, "Options");

        generated.Count.ShouldBe(original.Count);
        generated.GetterCalls.ShouldBe(original.GetterCalls);
        generated.SetterCalls.ShouldBe(original.SetterCalls);
        generated.SetterCalls.ShouldBe(1);
        generated.ReadOnlyGetterCalls.ShouldBe(original.ReadOnlyGetterCalls);
    }

    [Fact(DisplayName = "Generated scalar dictionary binding preserves existing entries and creates missing child-only entries")]
    public void BinderShouldPreserveScalarDictionaryEntries()
    {
        var configuration = new ConfigurationManager();
        foreach (var name in new[] { "Numbers", "Floating", "Optional", "Text", "Days", "Versions" })
            foreach (var key in new[] { "existing", "new", "empty" })
                configuration[$"Options:{name}:{key}:Ignored"] = "1";

        var original = configuration.GetSection("Options").Get<DictionaryOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<DictionaryOptions>(configuration, "Options");

        AssertEntries(generated.Numbers, original.Numbers);
        AssertEntries(generated.Floating, original.Floating);
        AssertEntries(generated.Optional, original.Optional);
        AssertEntries(generated.Text, original.Text);
        AssertEntries(generated.Days, original.Days);
        AssertEntries(generated.Versions, original.Versions);
    }

    [Theory(DisplayName = "Generated scalar collection entries preserve child-only construction and failure behavior")]
    [InlineData("int")]
    [InlineData("nullable")]
    [InlineData("string")]
    [InlineData("version")]
    public void BinderShouldPreserveScalarCollectionEntries(string kind)
    {
        var configuration = new ConfigurationManager { ["Options:List:0:Ignored"] = "1", ["Options:Array:0:Ignored"] = "1" };
        switch (kind)
        {
            case "int": AssertCollections<int>(configuration); break;
            case "nullable": AssertCollections<int?>(configuration); break;
            case "string": AssertCollections<string>(configuration); break;
            default: AssertCollections<Version>(configuration); break;
        }
    }

    [Theory(DisplayName = "Generated binding preserves missing scalar instance construction errors")]
    [InlineData("string")]
    [InlineData("uri")]
    [InlineData("culture")]
    public void BinderShouldPreserveScalarConstructionErrors(string kind)
    {
        var configuration = new ConfigurationManager { ["Options:Value:Ignored"] = "1" };
        switch (kind)
        {
            case "string": AssertConstructionFailure<string>(configuration); break;
            case "uri": AssertConstructionFailure<Uri>(configuration); break;
            default: AssertConstructionFailure<CultureInfo>(configuration); break;
        }
    }

    [Fact(DisplayName = "Generated scalar constructor parameters create a default value from a child-only section")]
    public void BinderShouldCreateScalarConstructorValues()
    {
        var configuration = new ConfigurationManager { ["Options:Count:Ignored"] = "1" };
        var original = configuration.GetSection("Options").Get<ConstructorOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<ConstructorOptions>(configuration, "Options");

        generated.Count.ShouldBe(original.Count);
        generated.Count.ShouldBe(0);
    }

    [Fact(DisplayName = "Generated child-only scalar binding does not create getter-only values")]
    public void BinderShouldPreserveReadOnlyNullScalars()
    {
        var configuration = new ConfigurationManager { ["Options:Text:Ignored"] = "1", ["Options:Optional:Ignored"] = "1" };
        var original = configuration.GetSection("Options").Get<ReadOnlyOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<ReadOnlyOptions>(configuration, "Options");

        generated.Text.ShouldBe(original.Text);
        generated.Text.ShouldBeNull();
        generated.Optional.ShouldBe(original.Optional);
        generated.Optional.ShouldBeNull();
    }

    private static void AssertEntries<T>(LookupDictionary<T> generated, LookupDictionary<T> original)
    {
        generated.ShouldBe(original);
        generated.Lookups.ShouldBe(original.Lookups);
        generated.Writes.ShouldBe(original.Writes);
    }

    private static void AssertCollections<T>(ConfigurationManager configuration)
    {
        var original = configuration.GetSection("Options").Get<CollectionOptions<T>>()!;
        var generated = OptionBindingRegistry.GetRequired<CollectionOptions<T>>(configuration, "Options");
        generated.List.ShouldBe(original.List);
        generated.Array.ShouldBe(original.Array);
    }

    private static void AssertConstructionFailure<T>(ConfigurationManager configuration)
    {
        var original = Record.Exception(() => configuration.GetSection("Options").Get<OptionBindingEdgeTests.ValueOptions<T>>());
        var generated = Record.Exception(() => OptionBindingRegistry.GetRequired<OptionBindingEdgeTests.ValueOptions<T>>(configuration, "Options"));
        original.ShouldBeOfType<InvalidOperationException>();
        generated.ShouldBeOfType<InvalidOperationException>();
        generated.Message.ShouldBe(original.Message);
    }

    public sealed class ScalarOptions : KafkaConsumerOption
    {
        public int Count { get; init; } = 7;
        public string Text { get; init; } = "initial";
        public int? Optional { get; init; } = 7;
        public int? EmptyOptional { get; init; }
        public DayOfWeek Day { get; init; } = DayOfWeek.Monday;
        public Guid Id { get; init; } = Guid.Parse("00000000-0000-0000-0000-000000000007");
        public TimeSpan Delay { get; init; } = TimeSpan.FromSeconds(7);
        public decimal Price { get; init; } = 7.5m;
        public Version Version { get; init; } = new(1, 2);
        public Version? EmptyVersion { get; init; }
        public Uri Address { get; init; } = new("https://example.com");
    }

    public sealed class AccessorOptions : KafkaConsumerOption
    {
        private int count = 7;
        public int Count
        {
            get { GetterCalls++; return count; }
            init { SetterCalls++; count = value; }
        }
        public int ReadOnlyCount { get { ReadOnlyGetterCalls++; return count; } }
        public int GetterCalls { get; private set; }
        public int SetterCalls { get; private set; }
        public int ReadOnlyGetterCalls { get; private set; }
    }

    public sealed class DictionaryOptions : KafkaConsumerOption
    {
        public LookupDictionary<int> Numbers { get; } = new() { ThrowOnLookup = false, ["existing"] = 7 };
        public LookupDictionary<double> Floating { get; } = new() { ThrowOnLookup = false, ["existing"] = 7.5 };
        public LookupDictionary<int?> Optional { get; } = new() { ThrowOnLookup = false, ["existing"] = 7, ["empty"] = null };
        public LookupDictionary<string> Text { get; } = new() { ThrowOnLookup = false, ["existing"] = "initial" };
        public LookupDictionary<DayOfWeek> Days { get; } = new() { ThrowOnLookup = false, ["existing"] = DayOfWeek.Monday };
        public LookupDictionary<Version> Versions { get; } = new() { ThrowOnLookup = false, ["existing"] = new(1, 2) };
    }

    public sealed class CollectionOptions<T> : KafkaConsumerOption
    {
        public List<T> List { get; init; } = [];
        public T[] Array { get; init; } = [];
    }

    public sealed class ConstructorOptions(int Count = 42) : KafkaConsumerOption
    {
        public int Count { get; } = Count;
    }

    public sealed class ReadOnlyOptions : KafkaConsumerOption
    {
        public string? Text { get; }
        public int? Optional { get; }
    }
}
