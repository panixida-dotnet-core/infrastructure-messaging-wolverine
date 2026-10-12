using System.Collections.ObjectModel;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class OptionBindingReadOnlyTests
{
    [Theory(DisplayName = "Generated binding validates configured read-only values without assigning them")]
    [InlineData("Count")]
    [InlineData("Optional")]
    [InlineData("Day")]
    [InlineData("Identifier")]
    [InlineData("Bytes")]
    [InlineData("Converted")]
    [InlineData("Number")]
    [InlineData("Point:X")]
    [InlineData("OptionalPoint:X")]
    [InlineData("Throwing")]
    public void BinderShouldRejectInvalidReadOnlyValues(string property)
    {
        var configuration = new ConfigurationManager { [$"Options:{property}"] = "invalid" };

        var original = Record.Exception(() => configuration.GetSection("Options").Get<ScalarOptions>());
        var generated = Record.Exception(() => OptionBindingRegistry.GetRequired<ScalarOptions>(configuration, "Options"));

        original.ShouldBeOfType<InvalidOperationException>();
        generated.ShouldBeOfType<InvalidOperationException>();
        generated.Message.ShouldContain("Options:" + property);
        ExceptionTypes(generated).ShouldBe(ExceptionTypes(original));
    }

    [Theory(DisplayName = "Generated binding preserves read-only values after successful conversion")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("42")]
    public void BinderShouldRetainReadOnlyValues(string? optional)
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Count"] = "42",
            ["Options:Optional"] = optional,
            ["Options:Day"] = "Friday",
            ["Options:Identifier"] = "d5a3dbfc-b56b-4a8d-a6c2-813675d30d74",
            ["Options:Bytes"] = "AgM=",
            ["Options:Converted"] = "configured",
            ["Options:Number"] = optional,
            ["Options:Point:X"] = "42",
            ["Options:OptionalPoint:X"] = "42",
            ["Options:MissingPoint:X"] = "invalid",
            ["Options:Throwing"] = "42",
        };

        var original = configuration.GetSection("Options").Get<ScalarOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<ScalarOptions>(configuration, "Options");

        generated.Count.ShouldBe(original.Count);
        generated.Count.ShouldBe(7);
        generated.Optional.ShouldBe(original.Optional);
        generated.Day.ShouldBe(original.Day);
        generated.Identifier.ShouldBe(original.Identifier);
        generated.Bytes.ShouldBe(original.Bytes);
        generated.Bytes.ShouldBe([1]);
        generated.Converted.ShouldBe(original.Converted);
        generated.Number.ShouldBe(original.Number);
        generated.Point.ShouldBe(original.Point);
        generated.OptionalPoint.ShouldBe(original.OptionalPoint);
        generated.MissingPoint.ShouldBeNull();
    }

    [Fact(DisplayName = "Generated getter-only read-only dictionaries update shared values and discard replacements")]
    public void BinderShouldUpdateSharedReadOnlyDictionaryValues()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Objects:existing:Name"] = "configured",
            ["Options:Objects:created:Name"] = "created",
            ["Options:Lists:existing:0"] = "2",
            ["Options:Maps:existing:number"] = "2",
            ["Options:Arrays:existing:0"] = "2",
            ["Options:Scalars:existing"] = "2",
            ["Options:Missing:created:Name"] = "created",
        };

        var original = configuration.GetSection("Options").Get<DictionaryOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<DictionaryOptions>(configuration, "Options");

        generated.Objects["existing"].Name.ShouldBe(original.Objects["existing"].Name);
        generated.Objects["existing"].Name.ShouldBe("configured");
        generated.Objects.Count.ShouldBe(original.Objects.Count);
        generated.Objects.ContainsKey("created").ShouldBeFalse();
        generated.Lists["existing"].ShouldBe(original.Lists["existing"]);
        generated.Lists["existing"].ShouldBe([1, 2]);
        generated.Maps["existing"].ShouldBe(original.Maps["existing"]);
        generated.Maps["existing"]["number"].ShouldBe(2);
        generated.Arrays["existing"].ShouldBe(original.Arrays["existing"]);
        generated.Arrays["existing"].ShouldBe([1]);
        generated.Scalars["existing"].ShouldBe(original.Scalars["existing"]);
        generated.Scalars["existing"].ShouldBe(1);
        generated.Missing.ShouldBeNull();
    }

    private static IEnumerable<Type> ExceptionTypes(Exception? exception)
    {
        while (exception is not null)
        {
            yield return exception.GetType();
            exception = exception.InnerException;
        }
    }

    public sealed class ScalarOptions : KafkaConsumerOption
    {
        public int Count { get; } = 7;
        public int? Optional { get; } = 8;
        public DayOfWeek Day { get; } = DayOfWeek.Monday;
        public Guid Identifier { get; }
        public byte[] Bytes { get; } = [1];
        public OptionBindingCompatibilityTests.ConvertedValue Converted { get; } = new("initial");
        public OptionBindingCompatibilityTests.ConvertedNumber? Number { get; } = new(7);
        public OptionBindingCompatibilityTests.Position Point { get; } = new(5);
        public OptionBindingCompatibilityTests.Position? OptionalPoint { get; } = new(5);
        public OptionBindingCompatibilityTests.Position? MissingPoint { get; }
        public int Throwing => throw new InvalidOperationException("Scalar getter must not be read.");
    }

    public sealed class DictionaryOptions : KafkaConsumerOption
    {
        public IReadOnlyDictionary<string, OptionBindingMemberTests.Payload> Objects { get; }
            = new ReadOnlyDictionary<string, OptionBindingMemberTests.Payload>(new Dictionary<string, OptionBindingMemberTests.Payload> { ["existing"] = new() });
        public IReadOnlyDictionary<string, List<int>> Lists { get; } = new Dictionary<string, List<int>> { ["existing"] = [1] };
        public IReadOnlyDictionary<string, Dictionary<string, int>> Maps { get; private set; }
            = new Dictionary<string, Dictionary<string, int>> { ["existing"] = new() { ["number"] = 1 } };
        public IReadOnlyDictionary<string, int[]> Arrays { get; } = new Dictionary<string, int[]> { ["existing"] = [1] };
        public IReadOnlyDictionary<string, int> Scalars { get; } = new Dictionary<string, int> { ["existing"] = 1 };
        public IReadOnlyDictionary<string, OptionBindingMemberTests.Payload>? Missing { get; }
    }
}
