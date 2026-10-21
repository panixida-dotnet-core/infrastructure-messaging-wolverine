using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

using static PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations.OptionBindingCompatibilityTests;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class OptionBindingFrameworkParityTests
{
    [Theory(DisplayName = "Generated binding writes back existing non-primitive scalar dictionary values with only children")]
    [InlineData("decimal")]
    [InlineData("enum")]
    [InlineData("guid")]
    [InlineData("datetime")]
    [InlineData("timespan")]
    public void BinderShouldPreserveNonPrimitiveScalarWrites(string kind)
    {
        switch (kind)
        {
            case "decimal": AssertScalarWrites<decimal>(); break;
            case "enum": AssertScalarWrites<DayOfWeek>(); break;
            case "guid": AssertScalarWrites<Guid>(); break;
            case "datetime": AssertScalarWrites<DateTime>(); break;
            default: AssertScalarWrites<TimeSpan>(); break;
        }
    }

    [Fact(DisplayName = "Generated binding falls back to property names for blank configuration aliases")]
    public void BinderShouldIgnoreBlankAliases()
    {
        var configuration = new ConfigurationManager
        {
            ["Options: "] = "ignored-space",
            ["Options:\t"] = "ignored-tab",
            ["Options:Space"] = "space-name",
            ["Options:Tab"] = "tab-name",
            ["Options:Empty"] = "empty-name",
        };

        var original = configuration.GetSection("Options").Get<BlankAliasOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<BlankAliasOptions>(configuration, "Options");

        generated.Space.ShouldBe(original.Space);
        generated.Space.ShouldBe("space-name");
        generated.Tab.ShouldBe(original.Tab);
        generated.Tab.ShouldBe("tab-name");
        generated.Empty.ShouldBe(original.Empty);
        generated.Empty.ShouldBe("empty-name");
    }

    [Fact(DisplayName = "Generated binding treats sealed and unsealed concrete read-only sets as ordinary models")]
    public void BinderShouldBindConcreteReadOnlySetProperties()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Unsealed:Name"] = "unsealed",
            ["Options:Unsealed:0"] = "2",
            ["Options:Sealed:Name"] = "sealed",
            ["Options:Sealed:0"] = "2",
            ["Options:Created:Name"] = "created",
        };

        var original = configuration.GetSection("Options").Get<ReadOnlySetOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<ReadOnlySetOptions>(configuration, "Options");

        generated.Unsealed.Name.ShouldBe(original.Unsealed.Name);
        generated.Unsealed.Name.ShouldBe("unsealed");
        generated.Unsealed.ShouldBe(original.Unsealed);
        generated.Unsealed.ShouldBe([1]);
        generated.Sealed.Name.ShouldBe(original.Sealed.Name);
        generated.Sealed.Name.ShouldBe("sealed");
        generated.Sealed.ShouldBe(original.Sealed);
        generated.Created.ShouldNotBeNull();
        generated.Created.Name.ShouldBe(original.Created!.Name);
        generated.Created.Name.ShouldBe("created");
    }

    private static void AssertScalarWrites<T>()
    {
        var configuration = new ConfigurationManager { ["Options:Values:existing:Ignored"] = "1" };
        var original = configuration.GetSection("Options").Get<ScalarDictionaryOptions<T>>()!;
        var generated = OptionBindingRegistry.GetRequired<ScalarDictionaryOptions<T>>(configuration, "Options");

        generated.Values.ShouldBe(original.Values);
        generated.Values.Writes.ShouldBe(original.Values.Writes);
        generated.Values.Writes.ShouldBe(1);
        generated.Values.Lookups.ShouldBe(original.Values.Lookups);
    }

    public sealed class ScalarDictionaryOptions<T> : KafkaConsumerOption
    {
        public LookupDictionary<T> Values { get; } = new() { ThrowOnLookup = false, ["existing"] = default! };
    }

    public sealed class BlankAliasOptions : KafkaConsumerOption
    {
        [ConfigurationKeyName(" ")]
        public string Space { get; init; } = "initial";
        [ConfigurationKeyName("\t")]
        public string Tab { get; init; } = "initial";
        [ConfigurationKeyName("")]
        public string Empty { get; init; } = "initial";
    }

    public sealed class ReadOnlySetOptions : KafkaConsumerOption
    {
        public ReadOnlySetPayload Unsealed { get; } = new();
        public SealedReadOnlySetPayload Sealed { get; } = new();
        public ReadOnlySetPayload? Created { get; init; }
    }

    public class ReadOnlySetPayload : IReadOnlySet<int>
    {
        private readonly HashSet<int> values = [1];
        public string Name { get; init; } = "initial";
        public int Count => values.Count;
        public bool Contains(int item) => values.Contains(item);
        public bool IsProperSubsetOf(IEnumerable<int> other) => values.IsProperSubsetOf(other);
        public bool IsProperSupersetOf(IEnumerable<int> other) => values.IsProperSupersetOf(other);
        public bool IsSubsetOf(IEnumerable<int> other) => values.IsSubsetOf(other);
        public bool IsSupersetOf(IEnumerable<int> other) => values.IsSupersetOf(other);
        public bool Overlaps(IEnumerable<int> other) => values.Overlaps(other);
        public bool SetEquals(IEnumerable<int> other) => values.SetEquals(other);
        public IEnumerator<int> GetEnumerator() => values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class SealedReadOnlySetPayload : ReadOnlySetPayload;
}
