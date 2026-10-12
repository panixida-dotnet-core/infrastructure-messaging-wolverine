using System.ComponentModel;
using System.Globalization;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class OptionBindingEdgeTests
{
    [Fact(DisplayName = "Generated binders are available when options are closed only inside a generic helper")]
    public void BinderShouldRegisterOptionsFromGenericHelpers()
    {
        var configuration = new ConfigurationManager { ["Options:Value"] = "42" };

        ReadHelper<int>(configuration).ShouldBe(42);
        ReadHelper<string>(configuration).ShouldBe("42");
    }

    [Fact(DisplayName = "Generated constructor binding recognizes nonpublic matching properties")]
    public void BinderShouldMatchNonpublicConstructorProperties()
    {
        var configuration = new ConfigurationManager { ["Options:Value:hidden"] = "configured" };

        var original = configuration.GetSection("Options").Get<ValueOptions<PrivatePropertyModel>>()!;
        var generated = OptionBindingRegistry.GetRequired<ValueOptions<PrivatePropertyModel>>(configuration, "Options");

        generated.Value.Name.ShouldBe(original.Value.Name);
        generated.Value.Name.ShouldBe("configured");
    }

    private static T ReadHelper<T>(ConfigurationManager configuration) => OptionBindingRegistry.GetRequired<ValueOptions<T>>(configuration, "Options").Value;

    [Fact(DisplayName = "Generated binding preserves null struct converter results in properties and mutable collections")]
    public void BinderShouldPreserveNullStructConversions()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:Value"] = "null",
            ["Options:Optional"] = "null",
            ["Options:ReadOnly"] = "null",
            ["Options:Values:0"] = "null",
            ["Options:Map:key"] = "null",
            ["Options:OptionalArray:0"] = "null",
        };

        var original = configuration.GetSection("Options").Get<NullConversionOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<NullConversionOptions>(configuration, "Options");

        generated.Value.ShouldBe(original.Value);
        generated.Value.ShouldBe(default);
        generated.Optional.ShouldBe(original.Optional);
        generated.Optional.ShouldBeNull();
        generated.ReadOnly.ShouldBe(original.ReadOnly);
        generated.Values.ShouldBe(original.Values);
        generated.Values.ShouldBe([default]);
        generated.Map.ShouldBe(original.Map);
        generated.Map["key"].ShouldBe(default);
        generated.OptionalArray.ShouldBe(original.OptionalArray);
        generated.OptionalArray.ShouldBe([null]);
    }

    [Theory(DisplayName = "Generated binding preserves null conversion failures for arrays and required constructor parameters")]
    [InlineData("array")]
    [InlineData("parameter")]
    public void BinderShouldPreserveNullConversionFailures(string kind)
    {
        var configuration = new ConfigurationManager { [kind == "array" ? "Options:Value:0" : "Options:Value"] = "null" };

        if (kind == "array") AssertFailure<ValueOptions<NullNumber[]>>(configuration);
        else AssertFailure<RequiredOptions>(configuration);
    }

    [Fact(DisplayName = "Generated binding uses optional constructor defaults after a null struct conversion")]
    public void BinderShouldUseOptionalDefaultsAfterNullConversion()
    {
        var configuration = new ConfigurationManager { ["Options:Value"] = "null" };

        var original = configuration.GetSection("Options").Get<OptionalOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<OptionalOptions>(configuration, "Options");

        generated.Value.ShouldBe(original.Value);
    }

    [Theory(DisplayName = "Generated binding skips immutable getters for children but preserves leaf getter behavior")]
    [InlineData("array", "children")]
    [InlineData("enumerable", "children")]
    [InlineData("collection", "children")]
    [InlineData("list", "children")]
    [InlineData("bytes", "children")]
    [InlineData("array", "leaf")]
    [InlineData("list", "leaf")]
    [InlineData("bytes", "leaf")]
    [InlineData("array", "invalid")]
    [InlineData("list", "invalid")]
    public void BinderShouldPreserveImmutableGetterAccess(string kind, string shape)
    {
        var value = shape == "leaf" ? string.Empty : "1";
        var configuration = new ConfigurationManager { [shape == "children" ? "Options:Value:0" : "Options:Value"] = value };
        switch (kind)
        {
            case "array": AssertGetter<ThrowingGetterOptions<int[]>>(configuration, shape); break;
            case "enumerable": AssertGetter<ThrowingGetterOptions<IEnumerable<int>>>(configuration, shape); break;
            case "collection": AssertGetter<ThrowingGetterOptions<IReadOnlyCollection<int>>>(configuration, shape); break;
            case "list": AssertGetter<ThrowingGetterOptions<IReadOnlyList<int>>>(configuration, shape); break;
            default: AssertGetter<ThrowingGetterOptions<byte[]>>(configuration, shape); break;
        }
    }

    [Theory(DisplayName = "Generated binding preserves constructor validation error messages")]
    [InlineData("private")]
    [InlineData("multiple")]
    [InlineData("missing-properties")]
    [InlineData("ref")]
    public void BinderShouldPreserveConstructorValidation(string kind)
    {
        var configuration = new ConfigurationManager { ["Options:Value:Name"] = "configured" };
        switch (kind)
        {
            case "private": AssertFailure<ValueOptions<PrivateModel>>(configuration); break;
            case "multiple": AssertFailure<ValueOptions<MultipleModel>>(configuration); break;
            case "missing-properties": AssertFailure<ValueOptions<MissingPropertiesModel>>(configuration); break;
            default: AssertFailure<ValueOptions<RefModel>>(configuration); break;
        }
    }

    private static void AssertGetter<T>(ConfigurationManager configuration, string shape) where T : class
    {
        if (shape != "leaf")
        {
            configuration.GetSection("Options").Get<T>().ShouldNotBeNull();
            OptionBindingRegistry.GetRequired<T>(configuration, "Options").ShouldNotBeNull();
        }
        else AssertFailure<T>(configuration);
    }

    private static void AssertFailure<T>(ConfigurationManager configuration) where T : class
    {
        var original = Record.Exception(() => configuration.GetSection("Options").Get<T>());
        var generated = Record.Exception(() => OptionBindingRegistry.GetRequired<T>(configuration, "Options"));

        original.ShouldNotBeNull();
        generated.ShouldNotBeNull();
        generated.GetType().ShouldBe(original.GetType());
        generated.Message.ShouldBe(original.Message);
    }

    public sealed class ValueOptions<T> : KafkaConsumerOption
    {
        public T Value { get; init; } = default!;
    }

    public sealed class NullConversionOptions : KafkaConsumerOption
    {
        public NullNumber Value { get; init; } = new(7);
        public NullNumber? Optional { get; init; } = new(7);
        public NullNumber ReadOnly { get; } = new(7);
        public List<NullNumber> Values { get; init; } = [];
        public Dictionary<string, NullNumber> Map { get; init; } = [];
        public NullNumber?[] OptionalArray { get; init; } = [];
    }

    public sealed class RequiredOptions(NullNumber Value) : KafkaConsumerOption
    {
        public NullNumber Value { get; } = Value;
    }

    public sealed class OptionalOptions(NullNumber Value = default) : KafkaConsumerOption
    {
        public NullNumber Value { get; } = Value;
    }

    public sealed class ThrowingGetterOptions<T> : KafkaConsumerOption
    {
        public T Value => throw new InvalidOperationException($"Unexpected getter for {TopicName}.");
    }

    public sealed class PrivateModel
    {
        private PrivateModel() { }
        public string Name { get; init; } = string.Empty;
    }

    public sealed class MultipleModel
    {
        public MultipleModel(string name) => Name = name;
        public MultipleModel(int name) => Name = name.ToString(CultureInfo.InvariantCulture);
        public string Name { get; init; }
    }

    public sealed class MissingPropertiesModel(string first, string second)
    {
        public string Name { get; init; } = first + second;
    }

    public sealed class RefModel
    {
        public RefModel(ref string name) => Name = name;
        public string Name { get; init; }
    }

    public sealed class PrivatePropertyModel(string hidden)
    {
        private string Hidden { get; } = hidden;
        public string Name => Hidden;
    }

    [TypeConverter(typeof(NullNumberConverter))]
    public readonly record struct NullNumber(int Value);

    public sealed class NullNumberConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);
        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => null;
    }
}
