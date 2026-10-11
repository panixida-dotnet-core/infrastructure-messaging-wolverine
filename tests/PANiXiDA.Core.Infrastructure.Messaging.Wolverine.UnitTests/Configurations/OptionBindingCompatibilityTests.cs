using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class OptionBindingCompatibilityTests
{
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
        generated.CoordinatesList.ShouldBe([default(Coordinates)]);
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
    public struct NonConvertingPoint
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

    public struct Position(int x)
    {
        public int X { get; init; } = x + 100;
        public int Y { get; init; } = 200;
    }

    public readonly record struct Coordinates(int X, int Y);
}
