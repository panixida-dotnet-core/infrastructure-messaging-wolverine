using System.ComponentModel;
using System.Globalization;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class OptionBindingLeafTests
{
    [Theory(DisplayName = "Generated binding probes custom conversion once for each value")]
    [InlineData("property")]
    [InlineData("parameter")]
    [InlineData("list")]
    [InlineData("array")]
    [InlineData("dictionary")]
    public void BinderShouldProbeConversionOnce(string location)
    {
        var configuration = new ConfigurationManager
        {
            [location is "list" or "array" or "dictionary" ? "Options:Value:0" : "Options:Value"] = "configured",
        };

        ProbeConverter.Probes = 0;
        var original = BindProbe(configuration, location, generated: false);
        ProbeConverter.Probes.ShouldBe(1);
        ProbeConverter.Probes = 0;
        var generated = BindProbe(configuration, location, generated: true);

        generated.ShouldBe(original);
        generated.ShouldBe("configured");
        ProbeConverter.Probes.ShouldBe(1);
    }

    [Fact(DisplayName = "Generated binding preserves public static properties and inherited static accessors")]
    public void BinderShouldBindStaticProperties()
    {
        var configuration = new ConfigurationManager { ["Options:Name"] = "configured", ["Options:Payload:Name"] = "nested" };
        StaticOptions.Name = "initial";
        StaticOptions.Payload.Name = "initial";
        _ = configuration.GetSection("Options").Get<StaticOptions>();
        var originalName = StaticOptions.Name;
        var originalPayload = StaticOptions.Payload.Name;
        StaticOptions.Name = "initial";
        StaticOptions.Payload.Name = "initial";

        _ = OptionBindingRegistry.GetRequired<StaticOptions>(configuration, "Options");

        StaticOptions.Name.ShouldBe(originalName);
        StaticOptions.Payload.Name.ShouldBe(originalPayload);
    }

    [Fact(DisplayName = "Generated binding preserves missing constructor parameter error text")]
    public void BinderShouldPreserveMissingParameterMessage()
    {
        var configuration = new ConfigurationManager { ["Options:TopicName"] = "configured" };

        var original = Record.Exception(() => configuration.GetSection("Options").Get<ParameterOptions<string>>());
        var generated = Record.Exception(() => OptionBindingRegistry.GetRequired<ParameterOptions<string>>(configuration, "Options"));

        original.ShouldBeOfType<InvalidOperationException>();
        generated.ShouldBeOfType<InvalidOperationException>();
        generated.Message.ShouldBe(original.Message);
    }

    [Theory(DisplayName = "Generated binding calls null property setters only when binding supplies a value")]
    [InlineData("invalid", 0)]
    [InlineData("", 1)]
    [InlineData(null, 1)]
    public void BinderShouldPreserveNullSetterCalls(string? value, int setterCalls)
    {
        var configuration = new ConfigurationManager { ["Options:Value"] = value };

        var original = configuration.GetSection("Options").Get<NullSetterOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<NullSetterOptions>(configuration, "Options");

        original.SetterCalls.ShouldBe(setterCalls);
        generated.SetterCalls.ShouldBe(original.SetterCalls);
        generated.Value.ShouldBeNull();
    }

    [Theory(DisplayName = "Generated binding default-constructs structs in empty collection entries")]
    [InlineData("")]
    [InlineData(null)]
    public void BinderShouldDefaultConstructStructEntries(string? value)
    {
        var configuration = new ConfigurationManager { ["Options:Value:0"] = value };

        AssertStructEntries<ValueOptions<List<RequiredStruct>>, RequiredStruct>(configuration);
        AssertStructEntries<ValueOptions<List<OptionalStruct>>, OptionalStruct>(configuration);
        AssertStructEntries<ValueOptions<List<DefaultStruct>>, DefaultStruct>(configuration);
    }

    [Theory(DisplayName = "Generated binding preserves empty immutable interface failures and array success")]
    [InlineData("enumerable")]
    [InlineData("collection")]
    [InlineData("list")]
    [InlineData("array")]
    public void BinderShouldPreserveEmptySequenceBehavior(string kind)
    {
        var configuration = new ConfigurationManager { ["Options:Value"] = string.Empty };
        switch (kind)
        {
            case "enumerable": AssertEmptySequenceFailure<ValueOptions<IEnumerable<int>>>(configuration); break;
            case "collection": AssertEmptySequenceFailure<ValueOptions<IReadOnlyCollection<int>>>(configuration); break;
            case "list": AssertEmptySequenceFailure<ValueOptions<IReadOnlyList<int>>>(configuration); break;
            default:
                var original = configuration.GetSection("Options").Get<ValueOptions<int[]>>()!;
                var generated = OptionBindingRegistry.GetRequired<ValueOptions<int[]>>(configuration, "Options");
                generated.Value.ShouldBe(original.Value);
                generated.Value.ShouldBeEmpty();
                break;
        }
    }

    private static void AssertEmptySequenceFailure<T>(ConfigurationManager configuration) where T : class
    {
        var original = Record.Exception(() => configuration.GetSection("Options").Get<T>());
        var generated = Record.Exception(() => OptionBindingRegistry.GetRequired<T>(configuration, "Options"));

        original.ShouldBeOfType<ArgumentNullException>();
        generated.ShouldBeOfType<ArgumentNullException>();
        generated.Message.ShouldBe(original.Message);
    }

    private static void AssertStructEntries<TOptions, T>(ConfigurationManager configuration) where TOptions : ValueOptions<List<T>> where T : struct
    {
        var original = configuration.GetSection("Options").Get<TOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<TOptions>(configuration, "Options");

        original.Value.Count.ShouldBe(1);
        generated.Value.ShouldBe(original.Value);
    }

    private static string? BindProbe(ConfigurationManager configuration, string location, bool generated)
    {
        return location switch
        {
            "parameter" => Bind<ParameterOptions<ProbeValue>>(configuration, generated).Value?.Name,
            "list" => Bind<ValueOptions<List<ProbeValue>>>(configuration, generated).Value.FirstOrDefault()?.Name,
            "array" => Bind<ValueOptions<ProbeValue[]>>(configuration, generated).Value.FirstOrDefault()?.Name,
            "dictionary" => Bind<ValueOptions<Dictionary<string, ProbeValue>>>(configuration, generated).Value.GetValueOrDefault("0")?.Name,
            _ => Bind<ValueOptions<ProbeValue>>(configuration, generated).Value?.Name,
        };
    }

    private static T Bind<T>(ConfigurationManager configuration, bool generated) where T : class => generated
        ? OptionBindingRegistry.GetRequired<T>(configuration, "Options")
        : configuration.GetSection("Options").Get<T>()!;

    public class ValueOptions<T> : KafkaConsumerOption
    {
        public T Value { get; init; } = default!;
    }

    public sealed class ParameterOptions<T>(T Value) : KafkaConsumerOption
    {
        public T Value { get; } = Value;
    }

    public class StaticBaseOptions : KafkaConsumerOption
    {
        public static string Name { get; set; } = "initial";
        public static Payload Payload { get; } = new();
    }

    public sealed class StaticOptions : StaticBaseOptions;

    public sealed class NullSetterOptions : KafkaConsumerOption
    {
        private Payload? value;
        public Payload? Value
        {
            get => value;
            init { this.value = value; SetterCalls++; }
        }
        public int SetterCalls { get; private set; }
    }

    public sealed class Payload
    {
        public string Name { get; set; } = "initial";
    }

    public struct RequiredStruct(int value)
    {
        public int Value { get; set; } = value;
    }

    public struct OptionalStruct(int value = 42)
    {
        public int Value { get; set; } = value;
    }

    public struct DefaultStruct()
    {
        public int Value { get; set; } = 42;
    }

    [TypeConverter(typeof(ProbeConverter))]
    public sealed class ProbeValue
    {
        public string Name { get; init; } = "initial";
    }

    public sealed class ProbeConverter : TypeConverter
    {
        public static int Probes;
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => ++Probes == 1;
        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => new ProbeValue { Name = (string)value };
    }
}
